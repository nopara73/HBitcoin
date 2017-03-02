using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HBitcoin.KeyManagement;
using HBitcoin.MemPool;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using Stratis.Bitcoin.BlockPulling;

namespace HBitcoin.FullBlockSpv
{
    public class TrackingJob
    {
		public Safe Safe { get; }
	    public bool TrackDefaultSafe { get; }
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

	    private readonly SemaphoreSlim SemaphoreSave = new SemaphoreSlim(1, 1);
		private NodeConnectionParameters _connectionParameters;
		private static NodesGroup _nodes;
		private static LookaheadBlockPuller BlockPuller;
	    private MemPoolJob MemPoolJob;

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

		public TrackingJob(Safe safeToTrack, bool trackDefaultSafe = true, params SafeAccount[] accountsToTrack)
	    {
		    Safe = safeToTrack;
		    if(accountsToTrack == null || !accountsToTrack.Any())
			{
				Accounts = new HashSet<SafeAccount>();
			}
			else Accounts = new HashSet<SafeAccount>(accountsToTrack);

		    TrackDefaultSafe = trackDefaultSafe;

		    
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
			if (TrackDefaultSafe) UpdateSafeTrackingByPath(hdPathType);

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
				Dictionary<Transaction, int> transactions;

				// if didn't find in the chain, it's clean
				bool clean = !TrackingChain.TryFindTransactions(scriptPubkey, out transactions);

				// if found in mempool it's not clean
				if(MemPoolJob != null)
				{
					foreach(var tx in MemPoolJob.Transactions.Values)
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

			TrackingChain.TrackedTransactions.CollectionChanged += delegate { UpdateSafeTracking(); };
			UpdateSafeTracking();

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

			_nodes.ConnectedNodes.Removed += delegate { OnConnectedNodeCountChanged(); };
			_nodes.ConnectedNodes.Added += delegate { OnConnectedNodeCountChanged(); };
			_nodes.Connect();

			CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ctsToken);

		    var tasks = new HashSet<Task>
		    {
			    PeriodicSaveAsync(TimeSpan.FromMinutes(3), cts.Token),
				BlockPullerJobAsync(cts.Token)
			};

		    await Task.WhenAll(tasks).ConfigureAwait(false);

			await SaveAllAsync().ConfigureAwait(false);
			_nodes.Dispose();
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
