using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HBitcoin.KeyManagement;
using HBitcoin.MemPool;
using HBitcoin.WalletDisplay;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using Stratis.Bitcoin.BlockPulling;

namespace HBitcoin.FullBlockSpv
{
    public class WalletJob
    {
		public Safe Safe { get; }
	    public bool TracksDefaultSafe { get; }
		public HashSet<SafeAccount> Accounts { get; }

	    private int _estimatedCreationHeight = -1;
		/// <summary>
		/// -1 if unknown (eg. the header chain is not there yet)
		/// </summary>
		public int EstimatedCreationHeight
	    {
		    get
		    {
				// it's enough to estimate once
			    if(_estimatedCreationHeight != -1) return _estimatedCreationHeight;
			    else return _estimatedCreationHeight = EstimateSafeCreationHeight();
		    }
	    }

		/// <summary>
		/// -1 if unknown (eg. the header chain is not there yet)
		/// </summary>
		private int EstimateSafeCreationHeight()
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

	    public int BestHeight => TrackingChain.BestHeight;
		/// <summary>
		/// 
		/// </summary>
		/// <returns>-1 if no connected nodes</returns>
	    public async Task<int> GetBestConnectedNodeHeightAsync()
	    {
		    const int noHeight = -1;
		    if(ConnectedNodeCount == 0) return noHeight;

		    var tasks = new HashSet<Task<int>>();
		    foreach(var node in _nodes.ConnectedNodes)
		    {
			    tasks.Add(Task.Run(() => node.GetChain().Height));
		    }

		    await Task.WhenAll(tasks).ConfigureAwait(false);

		    return tasks.Select(t => t.Result).Concat(new[] { noHeight }).Max();
	    }

	    public int ConnectedNodeCount => _nodes.ConnectedNodes.Count;
		public event EventHandler ConnectedNodeCountChanged;
		private void OnConnectedNodeCountChanged() => ConnectedNodeCountChanged?.Invoke(this, EventArgs.Empty);

		private WalletState _state;
		public WalletState State
		{
			get { return _state; }
			private set
			{
				if (_state == value) return;
				OnStateChanged();
				_state = value;
			}
		}
		public event EventHandler StateChanged;
		private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

		private readonly SemaphoreSlim SemaphoreSave = new SemaphoreSlim(1, 1);
		private NodeConnectionParameters _connectionParameters;
		private static NodesGroup _nodes;
		private static LookaheadBlockPuller BlockPuller;
	    private MemPoolJob MemPoolJob;
		
		public ObservableDictionary<Script, ObservableCollection<ScriptPubKeyHistoryRecord>> SafeHistory = new ObservableDictionary<Script, ObservableCollection<ScriptPubKeyHistoryRecord>>();
		
		private const string WorkFolderPath = "FullBlockSpv";
		private string _addressManagerFilePath => Path.Combine(WorkFolderPath, $"AddressManager{Safe.Network}.dat");
		private string _headerChainFilePath => Path.Combine(WorkFolderPath, $"HeaderChain{Safe.Network}.dat");
		private string _trackingChainFolderPath => Path.Combine(WorkFolderPath, $"TrackingChain{Safe.Network}");

		#region SmartProperties
		private TrackingChain _trackingChain = null;
		public TrackingChain TrackingChain => GetTrackingChainAsync().Result;
		// This async getter is for clean exception handling
		private async Task<TrackingChain> GetTrackingChainAsync()
		{
			// if already in memory return it
			if (_trackingChain != null) return _trackingChain;

			// else load it
			_trackingChain = new TrackingChain(Safe.Network, HeaderChain);
			try
			{
				await _trackingChain.LoadAsync(_trackingChainFolderPath).ConfigureAwait(false);
			}
			catch
			{
				// Sync blockchain:
				_trackingChain = new TrackingChain(Safe.Network, HeaderChain);
			}

			return _trackingChain;
		}

		private AddressManager AddressManager
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

		public ConcurrentChain HeaderChain
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
		#endregion

		public WalletJob(Safe safeToTrack, bool trackDefaultSafe = true, params SafeAccount[] accountsToTrack)
	    {
		    Safe = safeToTrack;
		    if(accountsToTrack == null || !accountsToTrack.Any())
			{
				Accounts = new HashSet<SafeAccount>();
			}
			else Accounts = new HashSet<SafeAccount>(accountsToTrack);

		    TracksDefaultSafe = trackDefaultSafe;

		    State = WalletState.NotStarted;
	    }

		#region SafeTracking

		public const int MaxCleanAddressCount = 21;
	    private void UpdateSafeTracking()
		{
			UpdateSafeTrackingByHdPathType(HdPathType.Receive);
			UpdateSafeTrackingByHdPathType(HdPathType.Change);
			UpdateSafeTrackingByHdPathType(HdPathType.NonHardened);
		}

		private void UpdateSafeTrackingByHdPathType(HdPathType hdPathType)
		{
			if (TracksDefaultSafe) UpdateSafeTrackingByPath(hdPathType);

			foreach (var acc in Accounts)
			{
				UpdateSafeTrackingByPath(hdPathType, acc);
			}
		}

		private void UpdateSafeTrackingByPath(HdPathType hdPathType, SafeAccount acccount = null)
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
					if(MemPoolJob != null)
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

		public async Task StartAsync(CancellationToken  ctsToken)
	    {
			Directory.CreateDirectory(WorkFolderPath);

			TrackingChain.TrackedTransactions.CollectionChanged += delegate
			{
				UpdateSafeTracking();
				UpdateSafeHistory();
			};
			UpdateSafeTracking();
			UpdateSafeHistory();

			_connectionParameters = new NodeConnectionParameters();
			//So we find nodes faster
			_connectionParameters.TemplateBehaviors.Add(new AddressManagerBehavior(AddressManager));
			//So we don't have to load the chain each time we start
			_connectionParameters.TemplateBehaviors.Add(new ChainBehavior(HeaderChain));

			_nodes = new NodesGroup(Safe.Network, _connectionParameters,
				new NodeRequirement
				{
					RequiredServices = NodeServices.Network,
					MinVersion = ProtocolVersion.SENDHEADERS_VERSION
				});
			var bp = new NodesBlockPuller(HeaderChain, _nodes.ConnectedNodes);
			_connectionParameters.TemplateBehaviors.Add(new NodesBlockPuller.NodesBlockPullerBehavior(bp));
			_nodes.NodeConnectionParameters = _connectionParameters;
			BlockPuller = (LookaheadBlockPuller)bp;

			MemPoolJob = new MemPoolJob(_nodes, TrackingChain);
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

			_nodes.ConnectedNodes.Removed += delegate { OnConnectedNodeCountChanged(); };
			_nodes.ConnectedNodes.Added += delegate { OnConnectedNodeCountChanged(); };
			_nodes.Connect();

			CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ctsToken);

		    var tasks = new HashSet<Task>
		    {
			    PeriodicSaveAsync(TimeSpan.FromMinutes(3), cts.Token),
				BlockPullerJobAsync(cts.Token),
				MemPoolJob.StartAsync(cts.Token)
			};

		    await Task.WhenAll(tasks).ConfigureAwait(false);

		    State = WalletState.NotStarted;
			await SaveAllAsync().ConfigureAwait(false);
			_nodes.Dispose();
		}

	    private void UpdateSafeHistory()
	    {
		    var scriptPubKeys = TrackingChain.TrackedScriptPubKeys;
		    foreach(var scriptPubKey in scriptPubKeys)
		    {
				ObservableCollection<ScriptPubKeyHistoryRecord> records = new ObservableCollection<ScriptPubKeyHistoryRecord>();

				Dictionary<Transaction, int> receivedTransactions;
				Dictionary<Transaction, int> spentTransactions;

			    if(TryFindAllChainAndMemPoolTransactions(scriptPubKey, out receivedTransactions, out spentTransactions))
			    {
				    foreach(var tx in receivedTransactions)
					{
						var record = new ScriptPubKeyHistoryRecord();
						record.Amount = Money.Zero; //for now

					    record.TransactionId = tx.Key.GetHash();
					    record.Confirmed = tx.Value != -1;
					    if(!record.Confirmed)
					    {
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
								var spent = spentTransactions.FirstOrDefault(x => x.Key.GetHash() == input.PrevOut.Hash).Key;
								if(!spent.Equals(default(Transaction)))
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
				    }
			    }

				SafeHistory.Add(scriptPubKey, records);
			}
		}

	    /// <summary>
	    /// 
	    /// </summary>
	    /// <param name="scriptPubKey"></param>
	    /// <param name="receivedTransactions">int: block height</param>
	    /// <param name="spentTransactions">int: block height</param>
	    /// <returns></returns>
	    public bool TryFindAllChainAndMemPoolTransactions(Script scriptPubKey, out Dictionary<Transaction, int> receivedTransactions, out Dictionary<Transaction, int> spentTransactions)
	    {
			var found = false;
			receivedTransactions = new Dictionary<Transaction, int>();
			spentTransactions = new Dictionary<Transaction, int>();
			
			foreach (var tx in GetAllChainAndMemPoolTransactions())
			{
				// if already has that tx continue
				if (receivedTransactions.Keys.Any(x => x.GetHash() == tx.Key.GetHash()))
					continue;

				foreach (var output in tx.Key.Outputs)
				{
					if (output.ScriptPubKey.Equals(scriptPubKey))
					{
						receivedTransactions.Add(tx.Key, tx.Value);
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
						    spentTransactions.Add(tx.Key, tx.Value);
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
		public Dictionary<Transaction, int> GetAllChainAndMemPoolTransactions()
		{
			var transactions = new Dictionary<Transaction, int>();

			foreach (var tx in TrackingChain.TrackedTransactions)
			{
				Transaction foundTransaction;
				if (tx.Value != -1)
				{
					if (TrackingChain.TryFindTransaction(tx.Key, tx.Value, out foundTransaction))
					{
						transactions.Add(foundTransaction, tx.Value);
					}
				}
				else
				{
					if (MemPoolJob != null)
					{
						if (MemPoolJob.TryFindTransaction(tx.Key, out foundTransaction))
						{
							transactions.Add(foundTransaction, -1);
						}
					}
				}
			}

			return transactions;
		}

		#region BlockPulling
		private static int timeoutDownSec = 10;
		private async Task BlockPullerJobAsync(CancellationToken ctsToken)
		{
			while (true)
			{
				if (ctsToken.IsCancellationRequested)
				{
					return;
				}

				// the headerchain didn't catch up to the creationheight yet
				if(EstimatedCreationHeight == -1)
				{
					await Task.Delay(1000, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
					continue;
				}

				if (HeaderChain.Height < EstimatedCreationHeight)
				{
					await Task.Delay(1000, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
					continue;
				}

				int height;
				if (TrackingChain.BlockCount == 0)
				{
					height = EstimatedCreationHeight;
				}
				else if (HeaderChain.Height <= TrackingChain.BestHeight)
				{
					await Task.Delay(100, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
					continue;
				}
				else
				{
					height = TrackingChain.BestHeight + 1;
				}

				var chainedBlock = HeaderChain.GetBlock(height);
				BlockPuller.SetLocation(new ChainedBlock(chainedBlock.Previous.Header, chainedBlock.Previous.Height));
				Block block = null;
				CancellationTokenSource ctsBlockDownload = CancellationTokenSource.CreateLinkedTokenSource(
					new CancellationTokenSource(TimeSpan.FromSeconds(timeoutDownSec)).Token,
					ctsToken);
				try
				{
					block = await Task.Run(() => BlockPuller.NextBlock(ctsBlockDownload.Token)).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					if (ctsToken.IsCancellationRequested) return;
					
					if (timeoutDownSec > 180)
					{
						timeoutDownSec = 20;
						_nodes.Purge("no reason");
					}
					else timeoutDownSec = timeoutDownSec * 2; // adjust to the network speed
					continue;
				}

				//reorg test
				//if(new Random().Next(100) >= 60) block = null;

				if (block == null) // then reorg happened
				{
					Reorg();
					continue;
				}

				TrackingChain.Add(chainedBlock.Height, block);

				// check if chains are in sync, to be sure
				var bh = TrackingChain.BestHeight;
				for (int i = bh; i > bh - 6; i--)
				{
					if (!TrackingChain.Chain[i].MerkleProof.Header.GetHash()
					.Equals(HeaderChain.GetBlock(i).Header.GetHash()))
					{
						// something worng, reorg
						Reorg();
					}
				}
			}
		}
		private void Reorg()
		{
			HeaderChain.SetTip(HeaderChain.Tip.Previous);
			TrackingChain.ReorgOne();
		}
		#endregion

		#region Saving
		private async Task PeriodicSaveAsync(TimeSpan delay, CancellationToken ctsToken)
		{
			while (true)
			{
				if (ctsToken.IsCancellationRequested)
				{
					return;
				}
				await SaveAllAsync().ConfigureAwait(false);
				await Task.Delay(delay, ctsToken).ContinueWith(tsk => { }).ConfigureAwait(false);
			}
		}
		private async Task SaveAllAsync()
		{
			await SemaphoreSave.WaitAsync().ConfigureAwait(false);
			try
			{
				AddressManager.SavePeerFile(_addressManagerFilePath, Safe.Network);
				SaveHeaderChain();
			}
			finally
			{
				SemaphoreSave.Release();
			}

			await TrackingChain.SaveAsync(_trackingChainFolderPath).ConfigureAwait(false);
		}
		private void SaveHeaderChain()
		{
			using (var fs = File.Open(_headerChainFilePath, FileMode.Create))
			{
				HeaderChain.WriteTo(fs);
			}
		}
		#endregion
	}
}
