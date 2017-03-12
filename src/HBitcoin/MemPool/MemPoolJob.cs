using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConcurrentCollections;
using HBitcoin.FullBlockSpv;
using NBitcoin;
using NBitcoin.Protocol;

namespace HBitcoin.MemPool
{
	public static class MemPoolJob
	{
		private static MemPoolState _state = MemPoolState.NotStarted;

		public static MemPoolState State
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

		public static ConcurrentObservableHashSet<Transaction> TrackedTransactions = new ConcurrentObservableHashSet<Transaction>();

		public static ConcurrentHashSet<Transaction> Transactions { get; private set; } = new ConcurrentHashSet<Transaction>();

		public static async Task StartAsync(CancellationToken ctsToken)
		{
#pragma warning disable 4014
			ClearTransactionsWhenConfirmationJobAsync(ctsToken);
#pragma warning restore 4014

			while (true)
			{
				try
				{
					if(ctsToken.IsCancellationRequested)
					{
						Transactions.Clear();
						State = MemPoolState.NotStarted;
						return;
					}

					if(WalletJob.Nodes.ConnectedNodes.Count <= 3 || !WalletJob.ChainsInSync)
					{
						State = MemPoolState.WaitingForBlockchainSync;
						await Task.Delay(100, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
						continue;
					}

					State = MemPoolState.Syncing;

					_confirmationHappening = false;
					ConcurrentHashSet<Task> tasks = new ConcurrentHashSet<Task> {Task.CompletedTask};
					foreach(var node in WalletJob.Nodes.ConnectedNodes)
					{
						tasks.Add(FillTransactionsAsync(node, ctsToken));
					}

					await Task.WhenAll(tasks).ConfigureAwait(false);

					await Task.Delay(1000, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
				}
				catch(Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Ignoring {nameof(MemPoolJob)} exception:");
					System.Diagnostics.Debug.WriteLine(ex);
				}
			}
		}

		private static bool _confirmationHappening = false;
		private static int _lastSeenBlockHeight = 0;
		private static async Task ClearTransactionsWhenConfirmationJobAsync(CancellationToken ctsToken)
		{
			while(true)
			{
				if(ctsToken.IsCancellationRequested) return;

				while(_confirmationHappening)
				{
					if (ctsToken.IsCancellationRequested) return;
					await Task.Delay(10, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
				}

				var currentBlockHeight = WalletJob.BestHeight;
				if(_lastSeenBlockHeight == currentBlockHeight) continue;
				if(_lastSeenBlockHeight == 0) _lastSeenBlockHeight = currentBlockHeight;
				else _lastSeenBlockHeight++;
				_confirmationHappening = true;
				Transactions.Clear();

				// a block just confirmed, take out the ones we are concerned
				if (TrackedTransactions.Count == 0) continue;
				if(WalletJob.Tracker.TrackedTransactions.Count == 0) continue;
				IEnumerable<SmartTransaction> justConfirmedTransactions;
				try
				{
					justConfirmedTransactions = WalletJob.Tracker.TrackedTransactions
						.Where(x => x.Height.Equals(_lastSeenBlockHeight));
				}
				catch(ArgumentNullException)
				{
					continue;
				}
				
				foreach(var tx in justConfirmedTransactions)
				{
					foreach(var ttx in TrackedTransactions)
					{
						if (tx.GetHash().Equals(ttx.GetHash()))
							TrackedTransactions.TryRemove(ttx);
					}
				}

				await Task.Delay(10, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
			}
		}

		private static async Task FillTransactionsAsync(Node node, CancellationToken ctsToken)
			=> await Task.Run(() => FillTransactions(node, ctsToken)).ConfigureAwait(false);

		private static void FillTransactions(Node node, CancellationToken ctsToken)
		{
			if (ctsToken.IsCancellationRequested) return;

			try
			{
				if (!node.IsConnected) return;
				uint256[] txIds = node.GetMempool(ctsToken);
				if(ctsToken.IsCancellationRequested) return;

				var txIdsPieces = Util.Split(txIds, 500);
				foreach(var txIdsPiece in txIdsPieces)
				{
					if (!node.IsConnected) return;
					foreach (var tx in node.GetMempoolTransactions(txIdsPiece.ToArray(), ctsToken))
					{
						if(_confirmationHappening) return;
						if(ctsToken.IsCancellationRequested) return;

						Transactions.Add(tx);

						// todo handle malleated transactions
						// todo handle transactions those are dropping out of the mempool
						// if we track it, then add
						// only if -1 it's not confirmed or present
						IEnumerable<SmartTransaction> awaitedTransactions = WalletJob.Tracker.TrackedTransactions.Where(x => !x.Confirmed);
						// if we are interested in the tx
						if(awaitedTransactions.Select(x=>x.GetHash()).Contains(tx.GetHash()))
						{
							var alreadyTrackedTxids = TrackedTransactions.Select(x => x.GetHash());
							// if not already tracking the track
							if(!alreadyTrackedTxids.Contains(tx.GetHash()))
							{
								TrackedTransactions.TryAdd(tx);
							}
						}

						foreach(var spk in tx.Outputs.Select(x => x.ScriptPubKey))
						{
							// if we are tracking that scriptpubkey
							if(WalletJob.Tracker.TrackedScriptPubKeys.Contains(spk))
							{
								var alreadyTrackedTxids = TrackedTransactions.Select(x => x.GetHash());
								// if not already tracking the transaction the track
								if(!alreadyTrackedTxids.Contains(tx.GetHash()))
								{
									TrackedTransactions.TryAdd(tx);
								}
							}
						}
					}
				}
			}
			catch(OperationCanceledException)
			{
				return;
			}
			catch (InvalidOperationException)
			{
				return;
			}
			catch (Exception)
			{
				if (ctsToken.IsCancellationRequested) return;

				throw;
			}
		}
	}
}
