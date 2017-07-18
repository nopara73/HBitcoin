using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using System.Diagnostics;
using HBitcoin.FullBlockSpv;
using System.Threading.Tasks;

namespace HBitcoin.TumbleBit.Services.HBitcoin
{
	public class HBitcoinBroadcastService : IBroadcastService
	{
		public class Record
		{
			public int Expiration { get; set; }
			public Transaction Transaction { get; set; }
		}
		HBitcoinWalletCache _Cache;
		public HBitcoinBroadcastService(WalletJob walletJob, HBitcoinWalletCache cache, IRepository repository)
		{
			_walletJob = walletJob ?? throw new ArgumentNullException(nameof(walletJob));
			_Repository = repository ?? throw new ArgumentNullException(nameof(repository));
			_Cache = cache;
			_BlockExplorerService = new HBitcoinBlockExplorerService(walletJob, cache, repository);
		}


		private readonly HBitcoinBlockExplorerService _BlockExplorerService;
		public HBitcoinBlockExplorerService BlockExplorerService => _BlockExplorerService;

		private readonly IRepository _Repository;
		public IRepository Repository => _Repository;

		private readonly WalletJob _walletJob;

		public Record[] GetTransactions()
		{
			var transactions = Repository.List<Record>("Broadcasts");
			foreach(var tx in transactions)
				tx.Transaction.CacheHashes();
			return transactions.TopologicalSort(tx => transactions.Where(tx2 => tx.Transaction.Inputs.Any<TxIn>(input => input.PrevOut.Hash == tx2.Transaction.GetHash()))).ToArray();
		}
		public async Task<Transaction[]> TryBroadcastAsync()
		{
			return await Task.Run(() =>
			{
				uint256[] r = null;
				return TryBroadcast(ref r);
			});
		}
		public Transaction[] TryBroadcast(ref uint256[] knownBroadcasted)
		{
			var startTime = DateTimeOffset.UtcNow;
			var totalEntries = 0;
			var broadcasted = new List<Transaction>();

			var knownBroadcastedSet = new HashSet<uint256>(knownBroadcasted ?? new uint256[0]);
			var height = _Cache.BlockCount;
			foreach (var obj in _Cache.GetEntries())
			{
				if(obj.Confirmations > 0)
					knownBroadcastedSet.Add(obj.TransactionId);
			}

			foreach(var tx in GetTransactions())
			{
				totalEntries++;
				if(!knownBroadcastedSet.Contains(tx.Transaction.GetHash()) &&
					TryBroadcastCoreAsync(tx, height).Result)
				{
					broadcasted.Add(tx.Transaction);
				}
				knownBroadcastedSet.Add(tx.Transaction.GetHash());
			}
			knownBroadcasted = knownBroadcastedSet.ToArray();
			Debug.WriteLine($"Broadcasted {broadcasted.Count} transaction(s), monitoring {totalEntries} entries in {(long)(DateTimeOffset.UtcNow - startTime).TotalSeconds} seconds");
			return broadcasted.ToArray();
		}

		private async Task<bool> TryBroadcastCoreAsync(Record tx, int currentHeight)
		{
			if (currentHeight >= tx.Expiration)
				RemoveRecord(tx);

			//Happens when the caller does not know the previous input yet
			if (tx.Transaction.Inputs.Count == 0 || tx.Transaction.Inputs[0].PrevOut.Hash == uint256.Zero)
				return false;

			var isFinal = tx.Transaction.IsFinal(DateTimeOffset.UtcNow, currentHeight + 1);
			if (!isFinal || IsDoubleSpend(tx.Transaction))
				return false;

			var res = await _walletJob.SendTransactionAsync(tx.Transaction).ConfigureAwait(false);
			if (res.Success)
			{
				_Cache.ImportTransaction(tx.Transaction, 0);
				Debug.WriteLine($"Broadcasted {tx.Transaction.GetHash()}");
				return true;
			}
			else
			{
				return false;
			}			
		}

		private bool IsDoubleSpend(Transaction tx)
		{
			var spentInputs = new HashSet<OutPoint>(tx.Inputs.Select(txin => txin.PrevOut));
			foreach(var entry in _Cache.GetEntries())
			{
				if(entry.Confirmations > 0)
				{
					var walletTransaction = _Cache.GetTransaction(entry.TransactionId);
					foreach(var input in walletTransaction.Inputs)
					{
						if(spentInputs.Contains(input.PrevOut))
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		private void RemoveRecord(Record tx)
		{
			Repository.Delete<Record>("Broadcasts", tx.Transaction.GetHash().ToString());
			Repository.UpdateOrInsert<Transaction>("CachedTransactions", tx.Transaction.GetHash().ToString(), tx.Transaction, (a, b) => a);
		}

		public async Task<bool> BroadcastAsync(Transaction transaction)
		{
			var record = new Record
			{
				Transaction = transaction
			};
			var height = _Cache.BlockCount;
			//3 days expiration
			record.Expiration = height + (int)(TimeSpan.FromDays(3).Ticks / Network.Main.Consensus.PowTargetSpacing.Ticks);
			Repository.UpdateOrInsert<Record>("Broadcasts", transaction.GetHash().ToString(), record, (o, n) => o);
			return await TryBroadcastCoreAsync(record, height).ConfigureAwait(false);
		}

		public Transaction GetKnownTransaction(uint256 txId) => Repository.Get<Record>("Broadcasts", txId.ToString())?.Transaction ??
				   Repository.Get<Transaction>("CachedTransactions", txId.ToString());
	}
}
