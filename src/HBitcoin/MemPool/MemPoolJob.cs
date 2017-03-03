using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HBitcoin.FullBlockSpv;
using NBitcoin;
using NBitcoin.Protocol;

namespace HBitcoin.MemPool
{
	public class MemPoolJob
	{
		private readonly TrackingChain _chain;
		private readonly NodesGroup _nodes;

		private MemPoolState _state;
		public MemPoolState State
		{
			get { return _state; }
			private set
			{
				if(_state == value) return;
				OnStateChanged();
				_state = value;
			}
		}
		public event EventHandler StateChanged;
		private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

		public ObservableCollection<Transaction> TrackedTransactions = new ObservableCollection<Transaction>();

		public MemPoolJob(NodesGroup nodes, TrackingChain chain)
		{
			_chain = chain;
			_nodes = nodes;
			State = MemPoolState.NotStarted;
		}

		public ConcurrentDictionary<uint256, Transaction> Transactions { get; private set; } = new ConcurrentDictionary<uint256, Transaction>();

		public async Task StartAsync(CancellationToken ctsToken)
		{
#pragma warning disable 4014
			ClearTransactionsWhenConfirmationJobAsync(ctsToken);
#pragma warning restore 4014

			while (true)
			{
				if (ctsToken.IsCancellationRequested)
				{
					Transactions.Clear();
					State = MemPoolState.NotStarted;
					return;
				}

				if (_nodes.ConnectedNodes.Count <= 3 || !_chain.Synced)
				{
					State = MemPoolState.WaitingForBlockchainSync;
					await Task.Delay(100, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
					continue;
				}
				State = MemPoolState.Syncing;

				confirmationHappening = false;
				HashSet<Task> tasks = new HashSet<Task> { Task.CompletedTask };
				foreach (var node in _nodes.ConnectedNodes)
				{
					tasks.Add(FillTransactionsAsync(node, ctsToken));
				}

				await Task.WhenAll(tasks).ConfigureAwait(false);

				await Task.Delay(1000, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
			}
		}

		private bool confirmationHappening = false;

		private async Task ClearTransactionsWhenConfirmationJobAsync(CancellationToken ctsToken)
		{
			while (true)
			{
				if (ctsToken.IsCancellationRequested) return;

				if (!_chain.Synced)
				{
					if (Transactions.Count != 0)
					{
						confirmationHappening = true;
						Transactions.Clear();

						while(confirmationHappening)
						{
							await Task.Delay(10, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
						}
						if (ctsToken.IsCancellationRequested) return;

						// a block just confirmed, take out the ones we are concerned
						if(_chain.FullBlockBuffer.Count != 0)
						{
							foreach(Transaction tx in _chain.FullBlockBuffer[_chain.FullBlockBuffer.Keys.Max()].Transactions)
							{
								foreach(var ttx in TrackedTransactions)
								{
									if(tx.GetHash().Equals(ttx.GetHash()))
										TrackedTransactions.Remove(ttx);
								}
							}
						}
					}
				}

				await Task.Delay(10, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
			}
		}

		private async Task FillTransactionsAsync(Node node, CancellationToken ctsToken)
			=> await Task.Run(() => FillTransactions(node, ctsToken)).ConfigureAwait(false);

		private void FillTransactions(Node node, CancellationToken ctsToken)
		{
			if (ctsToken.IsCancellationRequested) return;

			try
			{
				uint256[] txIds = node.GetMempool(ctsToken);
				var txIdsPieces = Util.Split(txIds, 500);
				foreach (var txIdsPiece in txIdsPieces)
				{
					foreach (var tx in node.GetMempoolTransactions(txIdsPiece.ToArray(), ctsToken))
					{
						if (confirmationHappening) return;
						if (ctsToken.IsCancellationRequested) return;

						Transactions.AddOrReplace(tx.GetHash(), tx);

						// todo handle malleated transactions
						// todo handle transactions those are dropping out of the mempool
						// if we track it, then add
						// only if -1 it's not confirmed or present
						var awaitedTxids = _chain.TrackedTransactions.Where(x => x.Value == -1).Select(x => x.Key);
						// if we are interested in the tx
						if(awaitedTxids.Contains(tx.GetHash()))
						{
							var alreadyTrackedTxids = TrackedTransactions.Select(x => x.GetHash());
							// if not already tracking the track
							if(!alreadyTrackedTxids.Contains(tx.GetHash()))
							{
								TrackedTransactions.Add(tx);
							}
						}

						foreach(var spk in tx.Outputs.Select(x => x.ScriptPubKey))
						{
							// if we are tracking that scriptpubkey
							if(_chain.TrackedScriptPubKeys.Contains(spk))
							{
								var alreadyTrackedTxids = TrackedTransactions.Select(x => x.GetHash());
								// if not already tracking the transaction the track
								if (!alreadyTrackedTxids.Contains(tx.GetHash()))
								{
									TrackedTransactions.Add(tx);
								}
							}
						}
					}
				}
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}
}
