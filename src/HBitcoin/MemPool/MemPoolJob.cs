using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConcurrentCollections;
using HBitcoin.FullBlockSpv;
using NBitcoin;
using System.Diagnostics;

namespace HBitcoin.MemPool
{
	public class NewTransactionEventArgs : EventArgs
	{
		public Transaction Transaction { get; }

		public NewTransactionEventArgs(Transaction transaction)
		{
			Transaction = transaction;
		}
	}
	public static class MemPoolJob
    {
	    private static ConcurrentHashSet<uint256> _transactions = new ConcurrentHashSet<uint256>();
        public static ConcurrentHashSet<uint256> Transactions { get => _transactions; private set => _transactions = value; }
		private static ConcurrentHashSet<uint256> _notNeededTransactions = new ConcurrentHashSet<uint256>();

        public static event EventHandler<NewTransactionEventArgs> NewTransaction;
		private static void OnNewTransaction(Transaction transaction) => NewTransaction?.Invoke(null, new NewTransactionEventArgs(transaction));

	    public static bool SyncedOnce { get; private set; } = false;
		public static event EventHandler Synced;
		private static void OnSynced() => Synced?.Invoke(null, EventArgs.Empty);

		public static bool ForcefullyStopped { get; set; } = false;
		internal static bool Enabled { get; set; } = true;

        public static async Task StartAsync(CancellationToken ctsToken)
		{
			while (true)
			{
				try
				{
					if (ctsToken.IsCancellationRequested) return;

					if(!Enabled || ForcefullyStopped || WalletJob.Nodes.ConnectedNodes.Count <= 3)
					{
						_notNeededTransactions.Clear(); // should not grow infinitely
						await Task.Delay(100, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
						continue;
					}

					var currentMemPoolTransactions = await UpdateAsync(ctsToken).ConfigureAwait(false);
					if (ctsToken.IsCancellationRequested) return;

					// Clear the transactions from the previous cycle
					Transactions = new ConcurrentHashSet<uint256>(currentMemPoolTransactions);
					_notNeededTransactions.Clear();

					if(!SyncedOnce)
					{
						SyncedOnce = true;
					}
					OnSynced();
					
					await Task.Delay(TimeSpan.FromMinutes(3), ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
				}
				catch(OperationCanceledException)
				{
					continue;
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"Ignoring {nameof(MemPoolJob)} exception:");
					Debug.WriteLine(ex);
				}
			}
		}

	    private static async Task<IEnumerable<uint256>> UpdateAsync(CancellationToken ctsToken)
	    {
		    var txidsWeAlreadyHadAndFound = new HashSet<uint256>();

			foreach (var node in WalletJob.Nodes.ConnectedNodes)
		    {
                try
                {
					if (ctsToken.IsCancellationRequested) return txidsWeAlreadyHadAndFound;
                    if (!node.IsConnected) continue;

                    var txidsWeNeed = new HashSet<uint256>();
					var sw = new Stopwatch();
					sw.Start();
					var txidsOfNode = await Task.Run(() => node.GetMempool(ctsToken)).ConfigureAwait(false);
					sw.Stop();
					Debug.WriteLine($"GetMempool(), txs: {txidsOfNode.Count()}, secs: {sw.Elapsed.TotalSeconds}");
					foreach (var txid in txidsOfNode)
                    {
                        // if we had it in prevcycle note we found it again
                        if (Transactions.Contains(txid)) txidsWeAlreadyHadAndFound.Add(txid);
						else if(_notNeededTransactions.Contains(txid))
						{
							// we don't need, do nothing
						}
                        // if we didn't have it in prevcicle note we need it
                        else txidsWeNeed.Add(txid);
                    }					
                    var txIdsPieces = Util.Split(txidsWeNeed.ToArray(), 500);

                    if (ctsToken.IsCancellationRequested) continue;
                    if (!node.IsConnected) continue;

                    foreach (var txIdsPiece in txIdsPieces)
                    {
						sw.Restart();
						
						var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(21));
						var ctsTokenGetMempoolTransactions = CancellationTokenSource.CreateLinkedTokenSource(ctsToken, timeoutSource.Token);
						Transaction[] txPiece = await Task.Run(() => node.GetMempoolTransactions(txIdsPiece.ToArray(), ctsTokenGetMempoolTransactions.Token)).ConfigureAwait(false);
						sw.Stop();
						Debug.WriteLine($"GetMempoolTransactions(), asked txs: {txIdsPiece.Count()}, actual txs: {txPiece.Count()}, secs: {sw.Elapsed.TotalSeconds}");

						foreach (
                            var tx in
							txPiece)
                        {
                            if (!node.IsConnected) continue;
                            if (ctsToken.IsCancellationRequested) continue;

							// note we found it and add to unprocessed
							if (txidsWeAlreadyHadAndFound.Add(tx.GetHash()))
							{
								if (!_notNeededTransactions.Contains(tx.GetHash()))
								{
									if (Transactions.Add(tx.GetHash()))
									{
										OnNewTransaction(tx);
									}
								}
							}
                        }						
					}

					// if the node has very few transactions disconnect it					
					if (WalletJob.CurrentNetwork == Network.Main && txidsOfNode.Count() <= 1)
					{
						node.Disconnect();
						node.Dispose();
						Debug.WriteLine("Disconnected node, because it has too few transactions.");
					}
                }
				catch (OperationCanceledException ex)
				{
					if (!ctsToken.IsCancellationRequested)
					{
						Debug.WriteLine($"Node exception in MemPool, disconnect node, continue with next node:");
						Debug.WriteLine(ex);
						try
						{
							node.Disconnect();
							node.Dispose();
						}
						catch { }
					}
					continue;
				}
                catch (Exception ex)
                {
					Debug.WriteLine($"Node exception in MemPool, disconnect node, continue with next node:");
					Debug.WriteLine(ex);
					try
					{
						node.Disconnect();
						node.Dispose();
					}
					catch { }
                    continue;
                }

				Debug.WriteLine($"Mirrored a node's full MemPool. Local MemPool transaction count: {Transactions.Count}");
			}
			foreach(var notneeded in _notNeededTransactions)
			{
				txidsWeAlreadyHadAndFound.Remove(notneeded);
			}
		    return txidsWeAlreadyHadAndFound;
	    }

		public static void RemoveTransactions(IEnumerable<uint256> transactionsToRemove)
		{
			foreach(var tx in transactionsToRemove)
			{
				_notNeededTransactions.Add(tx);
			}
			if (Transactions.Count() == 0) return;
			foreach(var tx in transactionsToRemove)
			{
				Transactions.TryRemove(tx);
			}
		}

		public static bool TryAddNewTransaction(Transaction tx)
		{
			if (ForcefullyStopped) return false;
			if (!Enabled) return false;

			uint256 hash = tx.GetHash();
			if (!_notNeededTransactions.Contains(hash))
			{
				if (Transactions.Add(hash))
				{
					OnNewTransaction(tx);
					return true;
				}
			}
			return false;
		}
	}
}
