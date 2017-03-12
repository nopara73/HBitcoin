using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConcurrentCollections;
using HBitcoin.KeyManagement;
using HBitcoin.MemPool;
using HBitcoin.WalletDisplay;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using Stratis.Bitcoin.BlockPulling;

namespace HBitcoin.FullBlockSpv
{
	public static class WalletJob
	{
		public static Safe Safe { get; private set; }
		public static bool TracksDefaultSafe { get; private set; }
		public static ConcurrentHashSet<SafeAccount> SafeAccounts { get; private set; }

		private static int _CreationHeight = -1;

		/// <summary>
		/// -1 if unknown (eg. the header chain is not there yet)
		/// </summary>
		public static int CreationHeight
		{
			get
			{
				// it's enough to estimate once
				if(_CreationHeight != -1) return _CreationHeight;
				else return _CreationHeight = FindSafeCreationHeight();
			}
		}

		/// <summary>
		/// -1 if unknown (eg. the header chain is not there yet)
		/// </summary>
		private static int FindSafeCreationHeight()
		{
			try
			{
				var currTip = HeaderChain.Tip;
				var currTime = currTip.Header.BlockTime;

				// the chain didn't catch up yet
				if(currTime < Safe.EarliestPossibleCreationTime)
					return -1;

				// the chain didn't catch up yet
				if(currTime < Safe.CreationTime)
					return -1;

				while(currTime > Safe.CreationTime)
				{
					currTip = currTip.Previous;
					currTime = currTip.Header.BlockTime;
				}

				// when the current tip time is lower than the creation time of the safe let's estimate that to be the creation height
				return currTip.Height;
			}
			catch
			{
				return -1;
			}
		}

		public static int BestHeight => Tracker.BestHeight;

		public static int ConnectedNodeCount
		{
			get
			{
				if(Nodes == null) return 0;
				return Nodes.ConnectedNodes.Count;
			}
		}

		public static int MaxConnectedNodeCount
		{
			get
			{
				if(Nodes == null) return 0;
				return Nodes.MaximumNodeConnection;
			}
		}

		public static event EventHandler ConnectedNodeCountChanged;
		private static void OnConnectedNodeCountChanged() => ConnectedNodeCountChanged?.Invoke(null, EventArgs.Empty);

		private static WalletState _state;

		public static WalletState State
		{
			get { return _state; }
			private set
			{
				if(_state == value) return;
				_state = value;
				OnStateChanged();
			}
		}

		public static event EventHandler StateChanged;
		private static void OnStateChanged() => StateChanged?.Invoke(null, EventArgs.Empty);

		public static bool ChainsInSync => Tracker.BestHeight == HeaderChain.Height;

		private static readonly SemaphoreSlim SemaphoreSave = new SemaphoreSlim(1, 1);
		private static NodeConnectionParameters _connectionParameters;
		public static NodesGroup Nodes { get; private set; }
		private static LookaheadBlockPuller BlockPuller;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="account">if null then default safe, if doesn't contain, then exception</param>
		/// <returns></returns>
		public static IEnumerable<SafeHistoryRecord> GetSafeHistory(SafeAccount account = null)
		{
			AssertAccount(account);

			var safeHistory = new HashSet<SafeHistoryRecord>();

			var transactions = GetAllChainAndMemPoolTransactionsBySafeAccount(account);
			var scriptPubKeys = GetTrackedScriptPubKeysBySafeAccount(account);

			foreach(SmartTransaction transaction in transactions)
			{
				SafeHistoryRecord record = new SafeHistoryRecord();
				record.TransactionId = transaction.GetHash();
				record.BlockHeight = transaction.Height;
				// todo: the mempool could note when it seen the transaction the first time
				record.TimeStamp = !transaction.Confirmed
					? DateTimeOffset.UtcNow
					: HeaderChain.GetBlock(transaction.Height).Header.BlockTime;

				record.Amount = Money.Zero; //for now

				// how much came to our scriptpubkeys
				foreach(var output in transaction.Transaction.Outputs)
				{
					if(scriptPubKeys.Contains(output.ScriptPubKey))
						record.Amount += output.Value;
				}

				foreach(var input in transaction.Transaction.Inputs)
				{
					// do we have the input?
					SmartTransaction inputTransaction = transactions.FirstOrDefault(x => x.GetHash() == input.PrevOut.Hash);
					if(default(SmartTransaction) != inputTransaction)
					{
						// if yes then deduct from amount (bitcoin output cannot be partially spent)
						var prevOutput = inputTransaction.Transaction.Outputs[input.PrevOut.N];
						if(scriptPubKeys.Contains(prevOutput.ScriptPubKey))
						{
							record.Amount -= prevOutput.Value;
						}
					}
					// if no then whatever
				}

				safeHistory.Add(record);
			}

			return safeHistory.ToList().OrderBy(x => x.TimeStamp);
		}

		private static void AssertAccount(SafeAccount account)
		{
			if(account == null)
			{
				if(!TracksDefaultSafe)
					throw new NotSupportedException($"{nameof(TracksDefaultSafe)} cannot be {TracksDefaultSafe}");
			}
			else
			{
				if(!SafeAccounts.Any(x => x.Id == account.Id))
					throw new NotSupportedException($"{nameof(SafeAccounts)} does not contain the provided {nameof(account)}");
			}
		}

		public static HashSet<SmartTransaction> GetAllChainAndMemPoolTransactionsBySafeAccount(SafeAccount account = null)
		{
			HashSet<Script> trackedScriptPubkeys = GetTrackedScriptPubKeysBySafeAccount(account);
			var foundTransactions = new HashSet<SmartTransaction>();

			foreach(var spk in trackedScriptPubkeys)
			{
				HashSet<SmartTransaction> rec;
				HashSet<SmartTransaction> spent;

				if(TryFindAllChainAndMemPoolTransactions(spk, out rec, out spent))
				{
					foreach(var tx in rec)
					{
						foundTransactions.Add(tx);
					}
					foreach(var tx in spent)
					{
						foundTransactions.Add(tx);
					}
				}
			}

			return foundTransactions;
		}

		public static HashSet<Script> GetTrackedScriptPubKeysBySafeAccount(SafeAccount account = null)
		{
			var maxTracked = Tracker.TrackedScriptPubKeys.Count;
			var allPossiblyTrackedAddresses = new HashSet<BitcoinAddress>();
			foreach(var address in Safe.GetFirstNAddresses(maxTracked, HdPathType.Receive, account))
			{
				allPossiblyTrackedAddresses.Add(address);
			}
			foreach(var address in Safe.GetFirstNAddresses(maxTracked, HdPathType.Change, account))
			{
				allPossiblyTrackedAddresses.Add(address);
			}
			foreach(var address in Safe.GetFirstNAddresses(maxTracked, HdPathType.NonHardened, account))
			{
				allPossiblyTrackedAddresses.Add(address);
			}

			var actuallyTrackedScriptPubKeys = new HashSet<Script>();
			foreach(var address in allPossiblyTrackedAddresses)
			{
				if(Tracker.TrackedScriptPubKeys.Any(x => x == address.ScriptPubKey))
					actuallyTrackedScriptPubKeys.Add(address.ScriptPubKey);
			}

			return actuallyTrackedScriptPubKeys;
		}
	
		private const string WorkFolderPath = "FullBlockSpvData";
		private static string _addressManagerFilePath => Path.Combine(WorkFolderPath, $"AddressManager{Safe.Network}.dat");
		private static string _headerChainFilePath => Path.Combine(WorkFolderPath, $"HeaderChain{Safe.Network}.dat");
		private static string _trackerFolderPath => Path.Combine(WorkFolderPath, Safe.UniqueId);

		#region SmartProperties
		private static Tracker _tracker = null;
		public static Tracker Tracker => GetTrackerAsync().Result;
		// This async getter is for clean exception handling
		private static async Task<Tracker> GetTrackerAsync()
		{
			// if already in memory return it
			if (_tracker != null) return _tracker;

			// else load it
			_tracker = new Tracker(Safe.Network);
			try
			{
				await _tracker.LoadAsync(_trackerFolderPath).ConfigureAwait(false);
			}
			catch
			{
				// Sync blockchain:
				_tracker = new Tracker(Safe.Network);
			}

			return _tracker;
		}

		private static AddressManager AddressManager
		{
			get
			{
				if (_connectionParameters != null)
				{
					foreach (var behavior in _connectionParameters.TemplateBehaviors)
					{
						var addressManagerBehavior = behavior as AddressManagerBehavior;
						if (addressManagerBehavior != null)
							return addressManagerBehavior.AddressManager;
					}
				}
				SemaphoreSave.Wait();
				try
				{
					return AddressManager.LoadPeerFile(_addressManagerFilePath);
				}
				catch
				{
					return new AddressManager();
				}
				finally
				{
					SemaphoreSave.Release();
				}
			}
		}

		private static ConcurrentChain HeaderChain
		{
			get
			{
				if (_connectionParameters != null)
					foreach (var behavior in _connectionParameters.TemplateBehaviors)
					{
						var chainBehavior = behavior as ChainBehavior;
						if (chainBehavior != null)
							return chainBehavior.Chain;
					}
				var chain = new ConcurrentChain(Safe.Network);
				SemaphoreSave.Wait();
				try
				{
					chain.Load(File.ReadAllBytes(_headerChainFilePath));
				}
				catch
				{
					// ignored
				}
				finally
				{
					SemaphoreSave.Release();
				}

				return chain;
			}
		}

	    public static Network Network => Safe.Network;

	    #endregion

		public static void Init(Safe safeToTrack, bool trackDefaultSafe = true, params SafeAccount[] accountsToTrack)
	    {
		    Safe = safeToTrack;
		    if(accountsToTrack == null || !accountsToTrack.Any())
			{
				SafeAccounts = new ConcurrentHashSet<SafeAccount>();
			}
			else SafeAccounts = new ConcurrentHashSet<SafeAccount>(accountsToTrack);

		    TracksDefaultSafe = trackDefaultSafe;
			
		    State = WalletState.NotStarted;
	    }

		#region static SafeTracking

		public const int MaxCleanAddressCount = 21;
	    private static void UpdateSafeTracking()
		{
			UpdateSafeTrackingByHdPathType(HdPathType.Receive);
			UpdateSafeTrackingByHdPathType(HdPathType.Change);
			UpdateSafeTrackingByHdPathType(HdPathType.NonHardened);
		}

		private static void UpdateSafeTrackingByHdPathType(HdPathType hdPathType)
		{
			if (TracksDefaultSafe) UpdateSafeTrackingByPath(hdPathType);

			foreach (var acc in SafeAccounts)
			{
				UpdateSafeTrackingByPath(hdPathType, acc);
			}
		}

		private static void UpdateSafeTrackingByPath(HdPathType hdPathType, SafeAccount acccount = null)
		{
			int i = 0;
			var cleanCount = 0;
			while (true)
			{
				Script scriptPubkey = acccount == null ? Safe.GetAddress(i, hdPathType).ScriptPubKey : Safe.GetAddress(i, hdPathType, acccount).ScriptPubKey;

				Tracker.Track(scriptPubkey);

				// if didn't find in the chain, it's clean
				bool clean = Tracker.IsClean(scriptPubkey);

				// if still clean look in mempool
				if(clean)
				{
					// if found in mempool it's not clean
					if(MemPoolJob.State == MemPoolState.Syncing)
					{
						foreach(var tx in MemPoolJob.TrackedTransactions)
						{
							foreach(var output in tx.Outputs)
							{
								if(output.ScriptPubKey.Equals(scriptPubkey))
								{
									clean = false;
								}
							}
						}
					}
				}

				// if clean we found a clean, elevate cleancount and if max reached don't look for more
				if(clean)
				{
					cleanCount++;
					if (cleanCount > MaxCleanAddressCount) return;
				}

				i++;
			}
		}
		
		#endregion

		public static async Task StartAsync(CancellationToken  ctsToken)
		{
			Directory.CreateDirectory(WorkFolderPath);

			Tracker.TrackedTransactions.CollectionChanged += delegate
			{
				UpdateSafeTracking();
			};

			_connectionParameters = new NodeConnectionParameters();
			//So we find nodes faster
			_connectionParameters.TemplateBehaviors.Add(new AddressManagerBehavior(AddressManager));
			//So we don't have to load the chain each time we start
			_connectionParameters.TemplateBehaviors.Add(new ChainBehavior(HeaderChain));

			UpdateSafeTracking();

			Nodes = new NodesGroup(Safe.Network, _connectionParameters,
				new NodeRequirement
				{
					RequiredServices = NodeServices.Network,
					MinVersion = ProtocolVersion.SENDHEADERS_VERSION
				});
			var bp = new NodesBlockPuller(HeaderChain, Nodes.ConnectedNodes);
			_connectionParameters.TemplateBehaviors.Add(new NodesBlockPuller.NodesBlockPullerBehavior(bp));
			Nodes.NodeConnectionParameters = _connectionParameters;
			BlockPuller = (LookaheadBlockPuller)bp;
			
		    MemPoolJob.StateChanged += delegate
		    {
			    if(MemPoolJob.State == MemPoolState.WaitingForBlockchainSync)
			    {
				    State = WalletState.SyncingBlocks;
			    }
			    if(MemPoolJob.State == MemPoolState.Syncing)
			    {
				    State = WalletState.SyncingMempool;
			    }
		    };
		    MemPoolJob.TrackedTransactions.CollectionChanged += delegate
		    {
				UpdateSafeTracking();
		    };

			Nodes.ConnectedNodes.Removed += delegate { OnConnectedNodeCountChanged(); };
			Nodes.ConnectedNodes.Added += delegate { OnConnectedNodeCountChanged(); };

			Nodes.Connect();

			var tasks = new ConcurrentHashSet<Task>
		    {
			    PeriodicSaveAsync(TimeSpan.FromMinutes(3), ctsToken),
				BlockPullerJobAsync(ctsToken),
				MemPoolJob.StartAsync(ctsToken)
			};

			State = WalletState.SyncingBlocks;
			await Task.WhenAll(tasks).ConfigureAwait(false);

		    State = WalletState.NotStarted;
			await SaveAllChangedAsync().ConfigureAwait(false);
			Nodes.Dispose();
		}

	    /// <summary>
	    /// 
	    /// </summary>
	    /// <param name="scriptPubKey"></param>
	    /// <param name="receivedTransactions">int: block height</param>
	    /// <param name="spentTransactions">int: block height</param>
	    /// <returns></returns>
	    public static bool TryFindAllChainAndMemPoolTransactions(Script scriptPubKey, out HashSet<SmartTransaction> receivedTransactions, out HashSet<SmartTransaction> spentTransactions)
	    {
			var found = false;
			receivedTransactions = new HashSet<SmartTransaction>();
			spentTransactions = new HashSet<SmartTransaction>();
			
			foreach (var tx in GetAllChainAndMemPoolTransactions())
			{
				// if already has that tx continue
				if (receivedTransactions.Any(x => x.GetHash() == tx.GetHash()))
					continue;

				foreach (var output in tx.Transaction.Outputs)
				{
					if (output.ScriptPubKey.Equals(scriptPubKey))
					{
						receivedTransactions.Add(tx);
						found = true;
					}
				}
			}

		    if(found)
		    {
			    foreach(var tx in GetAllChainAndMemPoolTransactions())
			    {
				    // if already has that tx continue
				    if(spentTransactions.Any(x => x.GetHash() == tx.GetHash()))
					    continue;

				    foreach(var input in tx.Transaction.Inputs)
				    {
					    if(receivedTransactions.Select(x => x.GetHash()).Contains(input.PrevOut.Hash))
					    {
						    spentTransactions.Add(tx);
						    found = true;
					    }
				    }
			    }
		    }

		    return found;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns>int: block height, -1 if mempool</returns>
		public static HashSet<SmartTransaction> GetAllChainAndMemPoolTransactions()
		{
			var transactions = new HashSet<SmartTransaction>();

			foreach (var tx in Tracker.TrackedTransactions)
			{
				if (tx.Confirmed)
				{
					transactions.Add(tx);
				}
				else
				{
					Transaction foundTransaction = MemPoolJob.TrackedTransactions.FirstOrDefault(x => x.GetHash() == tx.GetHash());
					if(foundTransaction != default(Transaction))
					{
						transactions.Add(tx);
					}
				}
			}

			return transactions;
		}

		#region BlockPulling
		private static async Task BlockPullerJobAsync(CancellationToken ctsToken)
		{
			const int currTimeoutDownSec = 360;
			while(true)
		    {
			    try
			    {
				    if(ctsToken.IsCancellationRequested)
				    {
					    return;
				    }

				    // the headerchain didn't catch up to the creationheight yet
				    if(CreationHeight == -1)
				    {
					    await Task.Delay(1000, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
					    continue;
				    }

				    if(HeaderChain.Height < CreationHeight)
				    {
					    await Task.Delay(1000, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
					    continue;
				    }

				    int height;
				    if(Tracker.BlockCount == 0)
				    {
					    height = CreationHeight;
				    }
				    else
					{
						int headerChainHeight = HeaderChain.Height;
						int trackerBestHeight = Tracker.BestHeight;
						int unprocessedBlockBestHeight = Tracker.UnprocessedBlockBuffer.BestHeight;
						if (headerChainHeight <= trackerBestHeight)
					    {
						    await Task.Delay(100, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
						    continue;
					    }
					    else if(headerChainHeight <= unprocessedBlockBestHeight)
					    {
						    await Task.Delay(100, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
						    continue;
					    }
					    else if(Tracker.UnprocessedBlockBuffer.Full)
					    {
						    await Task.Delay(100, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
						    continue;
					    }
					    else
					    {
						    height = Math.Max(trackerBestHeight, unprocessedBlockBestHeight) + 1;
					    }
				    }

					var chainedBlock = HeaderChain.GetBlock(height);
				    BlockPuller.SetLocation(new ChainedBlock(chainedBlock.Previous.Header, chainedBlock.Previous.Height));
				    Block block = null;
				    CancellationTokenSource ctsBlockDownload = CancellationTokenSource.CreateLinkedTokenSource(
					    new CancellationTokenSource(TimeSpan.FromSeconds(currTimeoutDownSec)).Token,
					    ctsToken);
				    var blockDownloadTask = Task.Run(() => BlockPuller.NextBlock(ctsBlockDownload.Token));
					block = await blockDownloadTask.ContinueWith(t =>
					{
						if (ctsToken.IsCancellationRequested) return null;
						if (t.IsCanceled || t.IsFaulted)
						{
							Nodes.Purge("no reason");
							Debug.WriteLine(
								$"Purging nodes, reason: couldn't download block in {currTimeoutDownSec} seconds.");
							return null;
						}
						return t.Result;
					}).ConfigureAwait(false);
					
					if (ctsToken.IsCancellationRequested) return;
					if (blockDownloadTask.IsCanceled || blockDownloadTask.IsFaulted)
						continue;

				    if(block == null) // then reorg happened
				    {
					    Reorg();
					    continue;
				    }

				    Tracker.AddOrReplaceBlock(chainedBlock.Height, block);
			    }
				catch (Exception ex)
				{
					Debug.WriteLine($"Ignoring {nameof(BlockPullerJobAsync)} exception:");
					Debug.WriteLine(ex);
				}
			}
		}

	    private static void Reorg()
		{
			HeaderChain.SetTip(HeaderChain.Tip.Previous);
			Tracker.ReorgOne();
		}
		#endregion

		#region Saving
		private static async Task PeriodicSaveAsync(TimeSpan delay, CancellationToken ctsToken)
		{
			while (true)
			{
				try
				{
					if (ctsToken.IsCancellationRequested) return;

					await SaveAllChangedAsync().ConfigureAwait(false);

					await Task.Delay(delay, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
				}
				catch(Exception ex)
				{
					Debug.WriteLine($"Ignoring {nameof(PeriodicSaveAsync)} exception:");
					Debug.WriteLine(ex);
				}
			}
		}

	    private static int _savedHeaderHeight = -1;
	    private static int _savedTrackingHeight = -1;

	    private static async Task SaveAllChangedAsync()
	    {
		    await SemaphoreSave.WaitAsync().ConfigureAwait(false);
			try
		    {
			    AddressManager.SavePeerFile(_addressManagerFilePath, Safe.Network);
				Debug.WriteLine($"Saved {nameof(AddressManager)}");

				if (_connectionParameters != null)
			    {
				    var headerHeight = HeaderChain.Height;
					if (_savedHeaderHeight == -1) _savedHeaderHeight = headerHeight;
				    if(headerHeight > _savedHeaderHeight)
				    {
					    SaveHeaderChain();
						Debug.WriteLine($"Saved {nameof(HeaderChain)} at height: {headerHeight}");
					    _savedHeaderHeight = headerHeight;
				    }
			    }
		    }
		    finally
		    {
			    SemaphoreSave.Release();
		    }

		    var trackingHeight = BestHeight;
		    if(_savedTrackingHeight == -1) _savedTrackingHeight = trackingHeight;
		    if(trackingHeight > _savedTrackingHeight)
		    {
			    await Tracker.SaveAsync(_trackerFolderPath).ConfigureAwait(false);
				Debug.WriteLine($"Saved {nameof(Tracker)} at height: {trackingHeight}");
			    _savedTrackingHeight = trackingHeight;
		    }
		}

	    private static void SaveHeaderChain()
		{
			using (var fs = File.Open(_headerChainFilePath, FileMode.Create))
			{
				HeaderChain.WriteTo(fs);
			}
		}
		#endregion

	    public static bool TryGetHeader(int height, out ChainedBlock creationHeader)
	    {
		    creationHeader = null;
		    try
		    {
			    if(_connectionParameters == null)
				    return false;

			    creationHeader = HeaderChain.GetBlock(height);
			    return true;
		    }
		    catch
		    {
				return false;
		    }
	    }

	    public static bool TryGetHeaderHeight(out int height)
	    {
		    height = default(int);
		    try
		    {
			    if(_connectionParameters == null)
				    return false;

			    height = HeaderChain.Height;
			    return true;
		    }
		    catch
		    {
				return false;
		    }
	    }
    }
}
