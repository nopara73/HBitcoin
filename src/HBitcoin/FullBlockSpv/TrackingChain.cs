using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

namespace HBitcoin.FullBlockSpv
{
    public class TrackingChain
    {
		#region Members

		public Network Network { get; private set; }
		public ObservableDictionary<int, TrackingBlock> Chain { get; } = new ObservableDictionary<int, TrackingBlock>();

	    /// <summary>
	    /// 
	    /// </summary>
	    /// <param name="scriptPubKey"></param>
	    /// <param name="receivedTransactions">int: block height</param>
	    /// <param name="spentTransactions">int: block height</param>
	    /// <returns></returns>
	    public bool TryFindConfirmedTransactions(Script scriptPubKey, out Dictionary<Transaction, int> receivedTransactions, out Dictionary<Transaction, int> spentTransactions)
	    {
		    var found = false;
		    receivedTransactions = new Dictionary<Transaction, int>();
			spentTransactions = new Dictionary<Transaction, int>();

			foreach (TrackingBlock block in Chain.Values)
		    {
			    foreach(var tx in block.TrackedTransactions)
			    {
					// if already has that tx continue
					if(receivedTransactions.Keys.Any(x => x.GetHash() == tx.GetHash()))
						continue;

					foreach(var output in tx.Outputs)
				    {
					    if(output.ScriptPubKey.Equals(scriptPubKey))
					    {
							receivedTransactions.Add(tx, block.Height);
						    found = true;
					    }
				    }
			    }
		    }

		    if(found)
		    {
			    foreach(TrackingBlock block in Chain.Values)
			    {
				    foreach(var tx in block.TrackedTransactions)
				    {
					    // if already has that tx continue
					    if(spentTransactions.Keys.Any(x => x.GetHash() == tx.GetHash()))
						    continue;

					    foreach(var input in tx.Inputs)
					    {
						    if(receivedTransactions.Keys.Select(x => x.GetHash()).Contains(input.PrevOut.Hash))
						    {
							    spentTransactions.Add(tx, block.Height);
							    found = true;
						    }
					    }
				    }
			    }
		    }

		    return found;
	    }

		/// <summary>
		/// 
		/// </summary>
		/// <param name="scriptPubKey"></param>
		/// <returns>if haven't got any fund yet</returns>
		public bool IsClean(Script scriptPubKey)
		{
			foreach (TrackingBlock block in Chain.Values)
			{
				foreach (var tx in block.TrackedTransactions)
				{
					if(tx.Outputs.Any(output => output.ScriptPubKey.Equals(scriptPubKey)))
					{
						return false;
					}
				}
			}

			return true;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="transactionId"></param>
		/// <param name="transaction">int: block height</param>
		/// <returns></returns>
		public bool TryFindTransaction(uint256 transactionId, out Tuple<Transaction, int> transaction)
		{
			transaction = null;

			foreach (TrackingBlock block in Chain.Values)
			{
				Transaction awaitedTransaction;
				if(TryFindTransaction(transactionId, block.Height, out awaitedTransaction))
				{
					transaction = new Tuple<Transaction, int>(awaitedTransaction, block.Height);
					return true;
				}
			}

			return false;
		}

	    public bool TryFindTransaction(uint256 transactionId, int blockHeight, out Transaction transaction)
	    {
			transaction = null;
			if (blockHeight == -1) return false;

		    try
		    {
			    var block = Chain[blockHeight];
			    foreach(var tx in block.TrackedTransactions)
			    {
				    if(tx.GetHash() == transactionId)
				    {
					    transaction = tx;
					    return true;
				    }
			    }
		    }
		    catch
		    {
				return false;
		    }
			return false;
	    }

	    /// <summary> int: block height, if tx is not found yet -1 </summary>
		public ObservableDictionary<uint256, int> TrackedTransactions { get; }
			= new ObservableDictionary<uint256, int>();
		public HashSet<Script> TrackedScriptPubKeys { get; }
			= new HashSet<Script>();
		private readonly ConcurrentDictionary<int, Block> _fullBlockBuffer = new ConcurrentDictionary<int, Block>();
		/// <summary>
		/// int: block height
		/// Max blocks in Memory is 50, removes the oldest one automatically if full
		///  </summary>
		public ConcurrentDictionary<int, Block> FullBlockBuffer
		{
			get
			{
				// Don't keep more than 50 blocks in memory
				while (_fullBlockBuffer.Count >= 50)
				{
					// Remove the oldest block
					var smallest = _fullBlockBuffer.Keys.Min();
					Block b;
					_fullBlockBuffer.TryRemove(smallest, out b);
				}
				return _fullBlockBuffer;
			}
		}

		public int WorstHeight => Chain.Count == 0 ? -1 : Chain.Values.Select(block => block.Height).Min();
		public int BestHeight => Chain.Count == 0 ? -1 : Chain.Values.Select(block => block.Height).Max();
		public int BlockCount => Chain.Count;

		#endregion

		#region Constructors

		private TrackingChain()
		{
		}
		public TrackingChain(Network network)
		{
			Network = network;
		}

		#endregion

		#region Tracking

		/// <summary> Track a transaction </summary>
		/// <returns>False if not confirmed. True if confirmed. If too old you need to resync the chain.</returns>
		public bool Track(uint256 transactionId)
		{
			// if already tracks it
			if (TrackedTransactions.Keys.Contains(transactionId))
			{
				// find it 
				var tracked = TrackedTransactions.First(x => x.Key.Equals(transactionId));
				// return false if not confirmed yet
				if (tracked.Value == -1) return false;
				// return true if confirmed
				else return true;
			}

			// if didn't track it yet add to tracked transactions
			TrackedTransactions.AddOrReplace(transactionId, -1);

			// search through the buffer, just in case
			foreach (var b in FullBlockBuffer.Values)
			{
				Transaction tx = b.Transactions.FirstOrDefault(x => transactionId.Equals(x.GetHash()));
				if (tx != default(Transaction))
				{
					// if found it set tx and blocks
					TrackingBlock trackingBlock =
					Chain.First(x => b.Header.GetHash().Equals(x.Value.MerkleProof.Header.GetHash())).Value;

					trackingBlock.TrackedTransactions.Add(tx);
					var transactionHashes = trackingBlock.MerkleProof.PartialMerkleTree.GetMatchedTransactions() as HashSet<uint256>;
					transactionHashes.Add(tx.GetHash());
					trackingBlock.MerkleProof = b.Filter(transactionHashes.ToArray());

					return true; // since it is confirmed
				}
			}
			return false; // since it didn't found, didn't confirmed
		}
		/// <param name="scriptPubKey">BitcoinAddress.ScriptPubKey</param>
		/// <param name="searchFullBlockBuffer">If true: it looks for transactions in the buffered full blocks in memory</param>
		public void Track(Script scriptPubKey, bool searchFullBlockBuffer = false)
		{
			TrackedScriptPubKeys.Add(scriptPubKey);

			if(searchFullBlockBuffer)
			{
				foreach(var block in FullBlockBuffer)
				{
					TrackIfFindRelatedTransactions(scriptPubKey, block.Key, block.Value);
				}
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="scriptPubKey"></param>
		/// <param name="height"></param>
		/// <param name="block"></param>
		/// <returns>true if found an already confirmed transaction in a block)</returns>
		private bool TrackIfFindRelatedTransactions(Script scriptPubKey, int height, Block block)
		{
			var found = false;
			foreach (var tx in block.Transactions)
			{
				foreach (var output in tx.Outputs)
				{
					if (output.ScriptPubKey.Equals(scriptPubKey))
					{
						TrackedTransactions.AddOrReplace(tx.GetHash(), height);
						found = true;
					}
				}
			}

			return found;
		}
		private HashSet<uint256> GetNotYetFoundTrackedTransactions()
		{
			var notFound = new HashSet<uint256>();
			foreach (var tx in TrackedTransactions)
			{
				if (tx.Value == -1)
				{
					notFound.Add(tx.Key);
				}
			}
			return notFound;
		}

		#endregion

		public void ReorgOne()
		{
			// remove the last block
			if (Chain.Count != 0)
			{
				TrackingBlock pb = Chain.FirstOrDefault(x => x.Value.Height == BestHeight).Value;

				if(default(TrackingBlock) != pb)
				{
					Chain.Remove(BestHeight);

					if(pb.TrackedTransactions.Count != 0)
					{
						// set the transactions to unconfirmed
						foreach(var txId in pb.TrackedTransactions.Select(x => x.GetHash()))
						{
							TrackedTransactions.AddOrReplace(txId, -1);
						}
					}
				}
			}

			// remove the last block from the buffer too
			Block b;
			if (FullBlockBuffer.Count() != 0)
			{
				FullBlockBuffer.TryRemove(FullBlockBuffer.Keys.Max(), out b);
			}
		}

		public void Add(int height, Block block)
		{
			// 1. Look for transactions, related to our scriptpubkeys
			foreach (var spk in TrackedScriptPubKeys)
			{
				TrackIfFindRelatedTransactions(spk, height, block);
			}

			// 2. Look for transactions, we are waiting to confirm
			FullBlockBuffer.AddOrReplace(height, block);
			HashSet<uint256> notYetFoundTrackedTransactions = GetNotYetFoundTrackedTransactions();
			HashSet<uint256> justFoundTransactions = new HashSet<uint256>();
			foreach (var txid in notYetFoundTrackedTransactions)
			{
				if (block.Transactions.Any(x => x.GetHash().Equals(txid)))
				{
					justFoundTransactions.Add(txid);
				}
			}

			// 3. Look for transactions, those are spending any of our transactions
			foreach(var txid in TrackedTransactions.Keys)
			{
				foreach(var tx in block.Transactions)
				{
					foreach(var input in tx.Inputs)
					{
						try
						{
							if(input.PrevOut.Hash == txid)
							{
								justFoundTransactions.Add(txid);
							}
						}
						catch
						{
							// this tx is strange, maybe this never happens, whatever
						}
					}
				}
			}

			MerkleBlock merkleProof = justFoundTransactions.Count == 0 ? block.Filter() : block.Filter(justFoundTransactions.ToArray());
			var trackingBlock = new TrackingBlock(height, merkleProof);
			foreach (var txid in justFoundTransactions)
			{
				foreach (var tx in block.Transactions)
				{
					if (tx.GetHash().Equals(txid))
						trackingBlock.TrackedTransactions.Add(tx);
				}
			}

			Chain.AddOrReplace(trackingBlock.Height, trackingBlock);
		}

		#region Saving

		private readonly SemaphoreSlim Saving = new SemaphoreSlim(1, 1);

	    private const string TrackedScriptPubKeysFileName = "TrackedScriptPubKeys.dat";
	    private const string TrackedTransactionsFileName = "TrackedTransactions.dat";
	    private const string TrackingChainFileName = "TrackingChain.dat";

		private static readonly byte[] blockSep = new byte[] { 0x10, 0x1A, 0x7B, 0x23, 0x5D, 0x12, 0x7D };
		public async Task SaveAsync(string trackingChainFolderPath)
		{
			await Saving.WaitAsync().ConfigureAwait(false);
			try
			{
				if (TrackedScriptPubKeys.Count > 0 || TrackedTransactions.Count > 0 || Chain.Count > 0)
				{
					Directory.CreateDirectory(trackingChainFolderPath);
				}

				if (TrackedScriptPubKeys.Count > 0)
				{
					File.WriteAllLines(
						Path.Combine(trackingChainFolderPath, TrackedScriptPubKeysFileName),
						TrackedScriptPubKeys.Select(x => x.ToString()));
				}

				if (TrackedTransactions.Count > 0)
				{
					File.WriteAllLines(
						Path.Combine(trackingChainFolderPath, TrackedTransactionsFileName),
						TrackedTransactions.Select(x => $"{x.Key}:{x.Value}"));
				}

				if (Chain.Count > 0)
				{
					byte[] toFile = Chain.Values.First().ToBytes();
					foreach (var block in Chain.Values.Skip(1))
					{
						toFile = toFile.Concat(blockSep).Concat(block.ToBytes()).ToArray();
					}

					File.WriteAllBytes(Path.Combine(trackingChainFolderPath, TrackingChainFileName),
						toFile);
				}
			}
			finally
			{
				Saving.Release();
			}
		}

		public async Task LoadAsync(string trackingChainFolderPath)
		{
			await Saving.WaitAsync().ConfigureAwait(false);
			try
			{
				if (!Directory.Exists(trackingChainFolderPath))
					throw new DirectoryNotFoundException($"No Blockchain found at {trackingChainFolderPath}");

				var tspb = Path.Combine(trackingChainFolderPath, TrackedScriptPubKeysFileName);
				if (File.Exists(tspb) && new FileInfo(tspb).Length != 0)
				{
					foreach (var line in File.ReadAllLines(tspb))
					{
						TrackedScriptPubKeys.Add(new Script(line));
					}
				}

				var tt = Path.Combine(trackingChainFolderPath, TrackedTransactionsFileName);
				if (File.Exists(tt) && new FileInfo(tt).Length != 0)
				{
					foreach (var line in File.ReadAllLines(tt))
					{
						var pieces = line.Split(':');
						TrackedTransactions.TryAdd(new uint256(pieces[0]), int.Parse(pieces[1]));
					}
				}

				var pbc = Path.Combine(trackingChainFolderPath, TrackingChainFileName);
				if (File.Exists(pbc) && new FileInfo(pbc).Length != 0)
				{
					foreach (var block in Util.Separate(File.ReadAllBytes(pbc), blockSep))
					{
						TrackingBlock pb = new TrackingBlock().FromBytes(block);

						Chain.TryAdd(pb.Height, pb);
					}
				}
			}
			finally
			{
				Saving.Release();
			}
		}

		#endregion
	}
}
