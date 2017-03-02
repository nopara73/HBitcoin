using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
				if (ctsToken.IsCancellationRequested)
				{
					return;
				}

				if (!_chain.Synced)
				{
					if (Transactions.Count != 0)
					{
						confirmationHappening = true;
						Transactions.Clear();
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
