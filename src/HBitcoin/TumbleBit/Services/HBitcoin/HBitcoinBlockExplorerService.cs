using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json.Linq;
using NBitcoin.DataEncoders;
using System.Threading;
using HBitcoin.FullBlockSpv;
using HBitcoin.Models;

namespace HBitcoin.TumbleBit.Services.HBitcoin
{
	public class HBitcoinBlockExplorerService : IBlockExplorerService
	{
		HBitcoinWalletCache _Cache;
		public HBitcoinBlockExplorerService(WalletJob walletJob, HBitcoinWalletCache cache, IRepository repo)
		{
			_walletJob = walletJob ?? throw new ArgumentNullException(nameof(walletJob));
			_repository = repo ?? throw new ArgumentNullException(nameof(repo));
			_Cache = cache ?? throw new ArgumentNullException(nameof(cache));
		}

		IRepository _repository;
		private readonly WalletJob _walletJob;

		public int GetCurrentHeight() => _Cache.BlockCount;

		public async Task<uint256> WaitBlockAsync(uint256 currentBlock, CancellationToken cancellation = default(CancellationToken))
		{
			while(true)
			{
				cancellation.ThrowIfCancellationRequested();
				WalletJob.TryGetHeader(_walletJob.BestHeight, out ChainedBlock header);
				var h = header.HashBlock;
				if (h != currentBlock)
				{
					_Cache.Refresh(h);
					return h;
				}
				await Task.Delay(5000, cancellation).ConfigureAwait(false);
			}
		}

		public TransactionInformation[] GetTransactions(Script scriptPubKey, bool withProof)
		{
			if(scriptPubKey == null)
				throw new ArgumentNullException(nameof(scriptPubKey));

			var address = scriptPubKey.GetDestinationAddress(_walletJob.Safe.Network);
			if(address == null)
				return new TransactionInformation[0];

			var walletTransactions = _Cache.GetEntries();
			var results = Filter(walletTransactions, !withProof, address);

			if (withProof)
			{
				foreach(var tx in results.ToList())
				{
					MerkleBlock proof = null;
					foreach (var smb in _walletJob.Tracker.MerkleChain)
					{
						if (smb.GetMatchedTransactions().Contains(tx.Transaction.GetHash()))
						{
							proof = smb.MerkleBlock;
						}
					}

					if (proof == null)
					{
						results.Remove(tx);
						continue;
					}

					tx.MerkleProof = proof;
				}
			}
			return results.ToArray();
		}

		private List<TransactionInformation> QueryWithListReceivedByAddress(bool withProof, BitcoinAddress address)
		{
			if (_walletJob.TryFindAllChainAndMemPoolTransactions(address.ScriptPubKey, out HashSet<SmartTransaction> received, out HashSet<SmartTransaction> spent))
			{
				var resultsSet = new HashSet<uint256>();
				var results = new List<TransactionInformation>();
				foreach (var stx in received.Union(spent))
				{
					var txId = stx.GetHash();
					//May have duplicates
					if (!resultsSet.Contains(txId))
					{
						var tx = GetTransaction(txId);
						if (tx == null || (withProof && tx.Confirmations == 0))
							continue;
						resultsSet.Add(txId);
						results.Add(tx);
					}
				}
				return results;
			}
			else return null;
		}

		private List<TransactionInformation> Filter(HBitcoinWalletEntry[] entries, bool includeUnconf, BitcoinAddress address)
		{
			var results = new List<TransactionInformation>();
			var resultsSet = new HashSet<uint256>();
			foreach (var obj in entries)
			{
				//May have duplicates
				if(!resultsSet.Contains(obj.TransactionId))
				{
					var confirmations = obj.Confirmations;
					var tx = _Cache.GetTransaction(obj.TransactionId);

					if(tx == null || (!includeUnconf && confirmations == 0))
						continue;

					if(tx.Outputs.Any(o => o.ScriptPubKey == address.ScriptPubKey) ||
					   tx.Inputs.Any(o => o.ScriptSig.GetSigner().ScriptPubKey == address.ScriptPubKey))
					{

						resultsSet.Add(obj.TransactionId);
						results.Add(new TransactionInformation
						{
							Transaction = tx,
							Confirmations = confirmations
						});
					}
				}
			}
			return results;
		}

		public TransactionInformation GetTransaction(uint256 txId)
		{
			try
			{
				SmartTransaction result = _walletJob.Tracker.TrackedTransactions.Where(x => x.GetHash() == txId).FirstOrDefault();
				if (result == default(SmartTransaction))
				{
					return null;
				}

				var tx = result.Transaction;
				var confCount = result.GetConfirmationCount(_walletJob.BestHeight);

				return new TransactionInformation
				{
					Confirmations = confCount,
					Transaction = tx
				};
			}
			catch { return null; }
		}

		public void Track(Script scriptPubkey)
		{
			_walletJob.Tracker.TrackedScriptPubKeys.Add(scriptPubkey);
		}

		public int GetBlockConfirmations(uint256 blockId)
		{
			return WalletJob.GetBlockConfirmations(blockId);
		}
	}
}
