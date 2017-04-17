using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConcurrentCollections;
using HBitcoin.FullBlockSpv;
using NBitcoin;

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
					if(ctsToken.IsCancellationRequested)
					{
						return;
					}

					if(WalletJob.Nodes.ConnectedNodes.Count <= 3 || !Enabled || ForcefullyStopped)
					{
						await Task.Delay(100, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
						continue;
					}

					var currentMemPoolTransactions = await UpdateAsync(ctsToken).ConfigureAwait(false);
					if (ctsToken.IsCancellationRequested) return;

					// Clear the transactions from the previous cycle
					Transactions = new ConcurrentHashSet<uint256>(currentMemPoolTransactions);

					if(!SyncedOnce)
					{
						SyncedOnce = true;
					}
					OnSynced();

					await Task.Delay(1000, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
				}
				catch(OperationCanceledException)
				{
					continue;
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Ignoring {nameof(MemPoolJob)} exception:");
					System.Diagnostics.Debug.WriteLine(ex);
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
                    foreach (var txid in await Task.Run(() => node.GetMempool(ctsToken)).ConfigureAwait(false))
                    {
                        // if we had it in prevcycle note we found it again
                        if (Transactions.Contains(txid)) txidsWeAlreadyHadAndFound.Add(txid);
                        // if we didn't have it in prevcicle note we need it
                        else txidsWeNeed.Add(txid);
                    }

                    var txIdsPieces = Util.Split(txidsWeNeed.ToArray(), 500);

                    if (ctsToken.IsCancellationRequested) continue;
                    if (!node.IsConnected) continue;

                    foreach (var txIdsPiece in txIdsPieces)
                    {
                        foreach (
                            var tx in
                            await Task.Run(() => node.GetMempoolTransactions(txIdsPiece.ToArray(), ctsToken)).ConfigureAwait(false))
                        {
                            if (!node.IsConnected) continue;
                            if (ctsToken.IsCancellationRequested) continue;

                            // note we found it and add to unprocessed
                            if (txidsWeAlreadyHadAndFound.Add(tx.GetHash()))
                                OnNewTransaction(tx);
                        }
                    }
                }
				catch (OperationCanceledException)
				{
					continue;
				}
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ignoring node exception, continuing iteration with next node:");
                    System.Diagnostics.Debug.WriteLine(ex);
                    continue;
                }
            }
		    return txidsWeAlreadyHadAndFound;
	    }
    }
}
