using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConcurrentCollections;
using NBitcoin;

namespace HBitcoin.FullBlockSpv
{
    public class TrackingChain
    {
		#region Members

		public Network Network { get; private set; }
		public ConcurrentObservableDictionary<int, TrackingBlock> Chain { get; } = new ConcurrentObservableDictionary<int, TrackingBlock>();

	    /// <summary>
	    /// 
	    /// </summary>
	    /// <param name="scriptPubKey"></param>
	    /// <param name="receivedTransactions">int: block height</param>
	    /// <param name="spentTransactions">int: block height</param>
	    /// <returns></returns>
	    public bool TryFindConfirmedTransactions(Script scriptPubKey, out ConcurrentDictionary<Transaction, int> receivedTransactions, out ConcurrentDictionary<Transaction, int> spentTransactions)
	    {
		    var found = false;
		    receivedTransactions = new ConcurrentDictionary<Transaction, int>();
			spentTransactions = new ConcurrentDictionary<Transaction, int>();

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
							receivedTransactions.AddOrReplace(tx, block.Height);
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
							    spentTransactions.AddOrReplace(tx, block.Height);
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
			if (BestHeight < blockHeight) return false;

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

	    /// <summary> int: block height, if tx is not in blockchain yet -1 </summary>
		public ConcurrentObservableDictionary<uint256, int> TrackedTransactions { get; }
			= new ConcurrentObservableDictionary<uint256, int>();
		public ConcurrentHashSet<Script> TrackedScriptPubKeys { get; }
			= new ConcurrentHashSet<Script>();

	    public readonly UnprocessedBlockBuffer UnprocessedBlockBuffer = new UnprocessedBlockBuffer();

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
			UnprocessedBlockBuffer.HaveBlocks += UnprocessedBlockBuffer_HaveBlocks;
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
			return false; // since it didn't found, didn't confirmed
		}
		/// <param name="scriptPubKey">BitcoinAddress.ScriptPubKey</param>
		public void Track(Script scriptPubKey)
		{
			TrackedScriptPubKeys.Add(scriptPubKey);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="scriptPubKey"></param>
		/// <param name="height"></param>
		/// <param name="block"></param>
		/// <returns>empty collection if not found any</returns>
		private ConcurrentHashSet<uint256> TrackIfFindRelatedTransactions(Script scriptPubKey, int height, Block block)
		{
			var found = new ConcurrentHashSet<uint256>();
			foreach (var tx in block.Transactions)
			{
				foreach (var output in tx.Outputs)
				{
					if (output.ScriptPubKey.Equals(scriptPubKey))
					{
						TrackedTransactions.AddOrReplace(tx.GetHash(), height);
						found.Add(tx.GetHash());
					}
				}
			}

			return found;
		}
		private ConcurrentHashSet<uint256> GetNotYetFoundTrackedTransactions()
		{
			var notFound = new ConcurrentHashSet<uint256>();
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
		}

	    public void AddOrReplaceBlock(int height, Block block)
	    {
		    UnprocessedBlockBuffer.TryAddOrReplace(height, block);
	    }

	    private void ProcessBlock(int height, Block block)
		{
			ConcurrentHashSet<uint256> justFoundTransactions = new ConcurrentHashSet<uint256>();

			// 1. Look for transactions, related to our scriptpubkeys
			foreach (var spk in TrackedScriptPubKeys)
			{
				foreach(var found in TrackIfFindRelatedTransactions(spk, height, block))
				{
					justFoundTransactions.Add(found);
				}
			}

			// 2. Look for transactions, we are waiting to confirm
			ConcurrentHashSet<uint256> notYetFoundTrackedTransactions = GetNotYetFoundTrackedTransactions();
			
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

			var trackingBlock = new TrackingBlock(height, block, justFoundTransactions.ToArray());
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



		private void UnprocessedBlockBuffer_HaveBlocks(object sender, EventArgs e)
		{
			int height;
			Block block;
			while (UnprocessedBlockBuffer.TryGetAndRemoveOldest(out height, out block))
			{
				ProcessBlock(height, block);
			}
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
					var path = Path.Combine(trackingChainFolderPath, TrackingChainFileName);

					if(File.Exists(path))
					{
						const string backupName = TrackingChainFileName + "_backup";
						var backupPath = Path.Combine(trackingChainFolderPath, backupName);
						File.Copy(path, backupPath, overwrite: true);
						File.Delete(path);
					}

					using(FileStream stream = File.OpenWrite(path))
					{
						var toFile = Chain.Values.First().ToBytes();
						await stream.WriteAsync(toFile, 0, toFile.Length).ConfigureAwait(false);
						foreach(var block in Chain.Values.Skip(1))
						{
							await stream.WriteAsync(blockSep, 0, blockSep.Length).ConfigureAwait(false);
							var blockBytes = block.ToBytes();
							await stream.WriteAsync(blockBytes, 0, blockBytes.Length).ConfigureAwait(false);
						}
					}

					//byte[] toFile = Chain.Values.First().ToBytes();
					//foreach (var block in Chain.Values.Skip(1))
					//{
					//	toFile = toFile.Concat(blockSep).Concat(block.ToBytes()).ToArray();
					//}

					//var path = Path.Combine(trackingChainFolderPath, TrackingChainFileName);
					//File.WriteAllBytes(path,
					//	toFile);
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
