using HBitcoin.FullBlockSpv;
using HBitcoin.Models;
using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HBitcoin.TumbleBit.Services.HBitcoin
{
	public class RPCWalletEntry
	{
		public uint256 TransactionId { get; set; }
		public int Confirmations { get; set; }
	}

	/// <summary>
	/// Workaround around slow Bitcoin Core RPC.
	/// We are refreshing the list of received transaction once per block.
	/// </summary>
	public class HBitcoinWalletCache
	{
		private readonly WalletJob _walletJob;
		private readonly IRepository _repository;
		public HBitcoinWalletCache(WalletJob walletJob, IRepository repository)
		{
			_walletJob = walletJob ?? throw new ArgumentNullException(nameof(walletJob));
			_repository = repository ?? throw new ArgumentNullException(nameof(repository));
		}

		volatile uint256 _RefreshedAtBlock;

		public void Refresh(uint256 currentBlock)
		{
			var refreshedAt = _RefreshedAtBlock;
			if(refreshedAt != currentBlock)
			{
				lock(_Transactions)
				{
					if(refreshedAt != currentBlock)
					{
						RefreshBlockCount();
						_Transactions = ListTransactions(ref _KnownTransactions);
						_RefreshedAtBlock = currentBlock;
					}
				}
			}
		}

		int _BlockCount;
		public int BlockCount
		{
			get
			{
				if(_BlockCount == 0)
				{
					RefreshBlockCount();
				}
				return _BlockCount;
			}
		}

		private void RefreshBlockCount()
		{
			Interlocked.Exchange(ref _BlockCount, _walletJob.BestHeight.Value);
		}

		public Transaction GetTransaction(uint256 txId)
		{
			var cached = GetCachedTransaction(txId);
			if(cached != null)
				return cached;
			var tx = FetchTransaction(txId);
			if(tx == null)
				return null;
			PutCached(tx);
			return tx;
		}

		ConcurrentDictionary<uint256, Transaction> _TransactionsByTxId = new ConcurrentDictionary<uint256, Transaction>();


		private Transaction FetchTransaction(uint256 txId)
		{
			try
			{
				//check in the wallet tx
				SmartTransaction result = _walletJob.Tracker.TrackedTransactions.Where(x => x.GetHash() == txId).FirstOrDefault();
				if (result == default(SmartTransaction))
				{
					return null;
				}
				return result.Transaction;
			}
			catch
			{
				return null;
			}
		}

		public RPCWalletEntry[] GetEntries()
		{
			lock(_Transactions)
			{
				return _Transactions.ToArray();
			}
		}

		private void PutCached(Transaction tx)
		{
			tx.CacheHashes();
			_repository.UpdateOrInsert("CachedTransactions", tx.GetHash().ToString(), tx, (a, b) => b);
			lock(_TransactionsByTxId)
			{
				_TransactionsByTxId.TryAdd(tx.GetHash(), tx);
			}
		}

		private Transaction GetCachedTransaction(uint256 txId)
		{

			if (_TransactionsByTxId.TryGetValue(txId, out Transaction tx))
			{
				return tx;
			}
			var cached = _repository.Get<Transaction>("CachedTransactions", txId.ToString());
			if(cached != null)
				_TransactionsByTxId.TryAdd(txId, cached);
			return cached;
		}


		List<RPCWalletEntry> _Transactions = new List<RPCWalletEntry>();
		HashSet<uint256> _KnownTransactions = new HashSet<uint256>();
		List<RPCWalletEntry> ListTransactions(ref HashSet<uint256> knownTransactions)
		{
			var array = new List<RPCWalletEntry>();
			knownTransactions = new HashSet<uint256>();
			var removeFromCache = new HashSet<uint256>(_TransactionsByTxId.Values.Select(tx => tx.GetHash()));
			var bestHeight = _walletJob.BestHeight;
			
			foreach (var stx in _walletJob.Tracker.TrackedTransactions)
			{
				var entry = new RPCWalletEntry
				{
					Confirmations = stx.Height == Height.MemPool ? 0 : bestHeight.Value - stx.Height.Value + 1,
					TransactionId = stx.GetHash()
				};
				removeFromCache.Remove(entry.TransactionId);
				if (knownTransactions.Add(entry.TransactionId))
				{
					array.Add(entry);
				}
			}
			foreach (var remove in removeFromCache)
			{
				_TransactionsByTxId.TryRemove(remove, out Transaction opt);
			}
			return array;
		}


		public void ImportTransaction(Transaction transaction, int confirmations)
		{
			PutCached(transaction);
			lock(_Transactions)
			{
				if(_KnownTransactions.Add(transaction.GetHash()))
				{
					_Transactions.Insert(0,
						new RPCWalletEntry
						{
							Confirmations = confirmations,
							TransactionId = transaction.GetHash()
						});
				}
			}
		}
	}
}
