using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
		public static ConcurrentHashSet<SafeAccount> Accounts { get; private set; }

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
				if (currTime < Safe.CreationTime)
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

	    public static int BestHeight => TrackingChain.BestHeight;

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
				if (_state == value) return;
				_state = value;
				OnStateChanged();
			}
		}
		public static event EventHandler StateChanged;
		private static void OnStateChanged() => StateChanged?.Invoke(null, EventArgs.Empty);

	    public static bool ChainsInSync => TrackingChain.BestHeight == HeaderChain.Height;

		private static readonly SemaphoreSlim SemaphoreSave = new SemaphoreSlim(1, 1);
		private static NodeConnectionParameters _connectionParameters;
	    public static NodesGroup Nodes { get; private set; }
	    private static LookaheadBlockPuller BlockPuller;
		
		public static ConcurrentObservableDictionary<Script, ObservableCollection<ScriptPubKeyHistoryRecord>> SafeHistory = new ConcurrentObservableDictionary<Script, ObservableCollection<ScriptPubKeyHistoryRecord>>();
		
		private const string WorkFolderPath = "FullBlockSpvData";
		private static string _addressManagerFilePath => Path.Combine(WorkFolderPath, $"AddressManager{Safe.Network}.dat");
		private static string _headerChainFilePath => Path.Combine(WorkFolderPath, $"HeaderChain{Safe.Network}.dat");
		private static string _trackingChainFolderPath => Path.Combine(WorkFolderPath, Safe.UniqueId);

		#region SmartProperties
		private static TrackingChain _trackingChain = null;
		public static TrackingChain TrackingChain => GetTrackingChainAsync().Result;
		// This async getter is for clean exception handling
		private static async Task<TrackingChain> GetTrackingChainAsync()
		{
			// if already in memory return it
			if (_trackingChain != null) return _trackingChain;

			// else load it
			_trackingChain = new TrackingChain(Safe.Network);
			try
			{
				await _trackingChain.LoadAsync(_trackingChainFolderPath).ConfigureAwait(false);
			}
			catch
			{
				// Sync blockchain:
				_trackingChain = new TrackingChain(Safe.Network);
			}

			return _trackingChain;
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
				Accounts = new ConcurrentHashSet<SafeAccount>();
			}
			else Accounts = new ConcurrentHashSet<SafeAccount>(accountsToTrack);

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

			foreach (var acc in Accounts)
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

				TrackingChain.Track(scriptPubkey);

				// if didn't find in the chain, it's clean
				bool clean = TrackingChain.IsClean(scriptPubkey);

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

			TrackingChain.TrackedTransactions.CollectionChanged += delegate
			{
				UpdateSafeTracking();
				UpdateSafeHistory();
			};

			_connectionParameters = new NodeConnectionParameters();
			//So we find nodes faster
			_connectionParameters.TemplateBehaviors.Add(new AddressManagerBehavior(AddressManager));
			//So we don't have to load the chain each time we start
			_connectionParameters.TemplateBehaviors.Add(new ChainBehavior(HeaderChain));

			UpdateSafeTracking();
			UpdateSafeHistory();

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
			    UpdateSafeHistory();
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

	    private static void UpdateSafeHistory()
	    {
		    var scriptPubKeys = TrackingChain.TrackedScriptPubKeys;
		    foreach(var scriptPubKey in scriptPubKeys)
		    {
				ObservableCollection<ScriptPubKeyHistoryRecord> records = new ObservableCollection<ScriptPubKeyHistoryRecord>();

				ConcurrentDictionary<Transaction, int> receivedTransactions;
				ConcurrentDictionary<Transaction, int> spentTransactions;

			    if(TryFindAllChainAndMemPoolTransactions(scriptPubKey, out receivedTransactions, out spentTransactions))
			    {
				    foreach(var tx in receivedTransactions)
					{
						var record = new ScriptPubKeyHistoryRecord();

						record.Amount = Money.Zero; //for now

					    record.TransactionId = tx.Key.GetHash();

						if(tx.Value == -1) 
					    {
							record.Confirmation = 0;

							var contains = false;
							// if already contains, don't modify timestamp
							if (SafeHistory.ContainsKey(scriptPubKey))
							{
								var rcrds = SafeHistory[scriptPubKey];
								foreach(var rcd in rcrds)
								{
									if(rcd.TransactionId == tx.Key.GetHash())
									{
										contains = true;
									}
								}
							}

							if (contains == false) record.TimeStamp = DateTimeOffset.UtcNow;
					    }
					    else
						{
							record.Confirmation = BestHeight - tx.Value + 1;
							record.TimeStamp = HeaderChain.GetBlock(tx.Value).Header.BlockTime;
					    }

						var coins = tx.Key.Outputs.AsCoins();
						foreach(var coin in coins)
						{
							if(coin.ScriptPubKey == scriptPubKey)
							{
								record.Amount += coin.Amount;
							}
						}

						if(spentTransactions.Count != 0)
						{
							foreach(var input in tx.Key.Inputs)
							{
								Transaction spent = spentTransactions.FirstOrDefault(x => x.Key.GetHash() == input.PrevOut.Hash).Key;
								if(default(Transaction) != spent)
								{
									var spentCoins = spent.Outputs.AsCoins();
									foreach(var spentCoin in spentCoins)
									{
										if (spentCoin.ScriptPubKey == scriptPubKey)
										{
											record.Amount -= spentCoin.Amount;
										}
									}
								}
							}
						}

						records.Add(record);
					}
			    }

				SafeHistory.AddOrReplace(scriptPubKey, records);
			}
		}

	    /// <summary>
	    /// 
	    /// </summary>
	    /// <param name="scriptPubKey"></param>
	    /// <param name="receivedTransactions">int: block height</param>
	    /// <param name="spentTransactions">int: block height</param>
	    /// <returns></returns>
	    public static bool TryFindAllChainAndMemPoolTransactions(Script scriptPubKey, out ConcurrentDictionary<Transaction, int> receivedTransactions, out ConcurrentDictionary<Transaction, int> spentTransactions)
	    {
			var found = false;
			receivedTransactions = new ConcurrentDictionary<Transaction, int>();
			spentTransactions = new ConcurrentDictionary<Transaction, int>();
			
			foreach (var tx in GetAllChainAndMemPoolTransactions())
			{
				// if already has that tx continue
				if (receivedTransactions.Keys.Any(x => x.GetHash() == tx.Key.GetHash()))
					continue;

				foreach (var output in tx.Key.Outputs)
				{
					if (output.ScriptPubKey.Equals(scriptPubKey))
					{
						receivedTransactions.AddOrReplace(tx.Key, tx.Value);
						found = true;
					}
				}
			}

		    if(found)
		    {
			    foreach(var tx in GetAllChainAndMemPoolTransactions())
			    {
				    // if already has that tx continue
				    if(spentTransactions.Keys.Any(x => x.GetHash() == tx.Key.GetHash()))
					    continue;

				    foreach(var input in tx.Key.Inputs)
				    {
					    if(receivedTransactions.Keys.Select(x => x.GetHash()).Contains(input.PrevOut.Hash))
					    {
						    spentTransactions.AddOrReplace(tx.Key, tx.Value);
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
		public static ConcurrentDictionary<Transaction, int> GetAllChainAndMemPoolTransactions()
		{
			var transactions = new ConcurrentDictionary<Transaction, int>();

			foreach (KeyValuePair<uint256, int> tx in TrackingChain.TrackedTransactions)
			{
				Transaction foundTransaction;
				if (tx.Value != -1)
				{
					if(TrackingChain.TryFindTransaction(tx.Key, tx.Value, out foundTransaction))
					{
						transactions.AddOrReplace(foundTransaction, tx.Value);
					}
				}
				else
				{
					if (MemPoolJob.State == MemPoolState.Syncing)
					{
						if (MemPoolJob.TryFindTransaction(tx.Key, out foundTransaction))
						{
							transactions.AddOrReplace(foundTransaction, -1);
						}
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
				    if(TrackingChain.BlockCount == 0)
				    {
					    height = CreationHeight;
				    }
				    else
					{
						int headerChainHeight = HeaderChain.Height;
						int trackingChainBestHeight = TrackingChain.BestHeight;
						int unprocessedBlockBestHeight = TrackingChain.UnprocessedBlockBuffer.BestHeight;
						if (headerChainHeight <= trackingChainBestHeight)
					    {
						    await Task.Delay(100, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
						    continue;
					    }
					    else if(headerChainHeight <= unprocessedBlockBestHeight)
					    {
						    await Task.Delay(100, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
						    continue;
					    }
					    else if(TrackingChain.UnprocessedBlockBuffer.Full)
					    {
						    await Task.Delay(100, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
						    continue;
					    }
					    else
					    {
						    height = Math.Max(trackingChainBestHeight, unprocessedBlockBestHeight) + 1;
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
							System.Diagnostics.Debug.WriteLine(
								$"Purging nodes, reason: couldn't download block in {nameof(currTimeoutDownSec)} seconds.");
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

				    TrackingChain.AddOrReplaceBlock(chainedBlock.Height, block);
			    }
			    catch(Exception ex)
			    {
					System.Diagnostics.Debug.WriteLine("Block pulling unhandled exception:");
					System.Diagnostics.Debug.WriteLine(ex);
					continue;
			    }
		    }
		}

	    private static void Reorg()
		{
			HeaderChain.SetTip(HeaderChain.Tip.Previous);
			TrackingChain.ReorgOne();
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
					System.Diagnostics.Debug.WriteLine("Periodic save failed, reason:");
					System.Diagnostics.Debug.WriteLine(ex);
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
				System.Diagnostics.Debug.WriteLine($"Saved {nameof(AddressManager)}");

				if (_connectionParameters != null)
			    {
				    var headerHeight = HeaderChain.Height;
				    if(headerHeight > _savedHeaderHeight)
				    {
					    SaveHeaderChain(); System.Diagnostics.Debug.WriteLine($"Saved {nameof(HeaderChain)} at height: {headerHeight}");
					}
			    }
		    }
		    finally
		    {
			    SemaphoreSave.Release();
		    }

		    var trackingHeight = BestHeight;
		    if(trackingHeight > _savedTrackingHeight)
		    {
			    await TrackingChain.SaveAsync(_trackingChainFolderPath).ConfigureAwait(false);
				System.Diagnostics.Debug.WriteLine($"Saved {nameof(TrackingChain)} at height: {trackingHeight}");
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
		    if(_connectionParameters == null)
			    return false;

			creationHeader = HeaderChain.GetBlock(height);
			return true;
		}

	    public static bool TryGetHeaderHeight(out int height)
	    {
		    height = default(int);
			if (_connectionParameters == null)
				return false;

		    height = HeaderChain.Height;
			return true;
	    }
    }
}
