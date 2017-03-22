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
	public class MemPoolJob
    {
	    public static ConcurrentHashSet<uint256> Transactions = new ConcurrentHashSet<uint256>();

		public static event EventHandler<NewTransactionEventArgs> NewTransaction;
		private static void OnNewTransaction(Transaction transaction) => NewTransaction?.Invoke(null, new NewTransactionEventArgs(transaction));
		
		public static bool _Synced = false;
		public static event EventHandler Synced;
		private static void OnSynced() => Synced?.Invoke(null, EventArgs.Empty);

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

					if(WalletJob.Nodes.ConnectedNodes.Count <= 3 || WalletJob.StallMemPool)
					{
						await Task.Delay(100, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
						continue;
					}

					var txidsWeAlreadyHadAndFound = new HashSet<uint256>();

					foreach(var node in WalletJob.Nodes.ConnectedNodes)
					{
						if(ctsToken.IsCancellationRequested) return;
						if(!node.IsConnected) continue;


						var txidsWeNeed = new HashSet<uint256>();
						foreach(var txid in await Task.Run(() => node.GetMempool(ctsToken)).ConfigureAwait(false))
						{
							// if we had it in prevcycle note we found it again
							if(Transactions.Contains(txid)) txidsWeAlreadyHadAndFound.Add(txid);
							// if we didn't have it in prevcicle note we need it
							else txidsWeNeed.Add(txid);
						}

						var txIdsPieces = Util.Split(txidsWeNeed.ToArray(), 500);

						if(ctsToken.IsCancellationRequested) continue;
						if(!node.IsConnected) continue;

						foreach(var txIdsPiece in txIdsPieces)
						{
							foreach(
								var tx in
								await Task.Run(() => node.GetMempoolTransactions(txIdsPiece.ToArray(), ctsToken)).ConfigureAwait(false))
							{
								if(!node.IsConnected) continue;
								if(ctsToken.IsCancellationRequested) continue;

								// note we found it and add to unprocessed
								if(txidsWeAlreadyHadAndFound.Add(tx.GetHash()))
									OnNewTransaction(tx);
							}
						}
					}

					// Clear the transactions from the previous cycle
					Transactions = new ConcurrentHashSet<uint256>(txidsWeAlreadyHadAndFound);

					if(!_Synced)
					{
						_Synced = true;
						OnSynced();
					}

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
	}
}
