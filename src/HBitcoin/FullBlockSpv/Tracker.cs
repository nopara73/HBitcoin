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
    public class Tracker
    {
		#region Members

		public Network Network { get; private set; }
		public ConcurrentHashSet<SmartMerkleBlock> MerkleChain { get; } = new ConcurrentHashSet<SmartMerkleBlock>();

	    /// <summary>
	    /// 
	    /// </summary>
	    /// <param name="scriptPubKey"></param>
	    /// <param name="receivedTransactions">int: block height</param>
	    /// <param name="spentTransactions">int: block height</param>
	    /// <returns></returns>
	    public bool TryFindConfirmedTransactions(Script scriptPubKey, out ConcurrentHashSet<SmartTransaction> receivedTransactions, out ConcurrentHashSet<SmartTransaction> spentTransactions)
	    {
		    var found = false;
		    receivedTransactions = new ConcurrentHashSet<SmartTransaction>();
			spentTransactions = new ConcurrentHashSet<SmartTransaction>();
			
			foreach(var tx in TrackedTransactions.Where(x=>x.Confirmed))
			{
				// if already has that tx continue
				if(receivedTransactions.Any(x => x.GetHash() == tx.GetHash()))
					continue;

				foreach(var output in tx.Transaction.Outputs)
				{
					if(output.ScriptPubKey.Equals(scriptPubKey))
					{
						receivedTransactions.Add(tx);
						found = true;
					}
				}
			}

		    if(found)
		    {
			    foreach(var tx in TrackedTransactions.Where(x => x.Confirmed))
			    {
				    // if already has that tx continue
				    if(spentTransactions.Any(x => x.GetHash() == tx.GetHash()))
					    continue;

				    foreach(var input in tx.Transaction.Inputs)
				    {
					    if(receivedTransactions.Select(x => x.GetHash()).Contains(input.PrevOut.Hash))
					    {
						    spentTransactions.Add(tx);
						    found = true;
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
			foreach(var tx in TrackedTransactions.Where(x => x.Confirmed))
			{
				if(tx.Transaction.Outputs.Any(output => output.ScriptPubKey.Equals(scriptPubKey)))
				{
					return false;
				}
			}

			return true;
		}

	    public ConcurrentObservableHashSet<SmartTransaction> TrackedTransactions { get; }
			= new ConcurrentObservableHashSet<SmartTransaction>();
		public ConcurrentHashSet<Script> TrackedScriptPubKeys { get; }
			= new ConcurrentHashSet<Script>();

	    public readonly UnprocessedBlockBuffer UnprocessedBlockBuffer = new UnprocessedBlockBuffer();

		public int WorstHeight => MerkleChain.Count == 0 ? -1 : MerkleChain.Select(block => block.Height).Min();
		public int BestHeight => MerkleChain.Count == 0 ? -1 : MerkleChain.Select(block => block.Height).Max();
		public int BlockCount => MerkleChain.Count;

		#endregion

		#region Constructors

		private Tracker()
		{
		}
		public Tracker(Network network)
		{
			Network = network;
			UnprocessedBlockBuffer.HaveBlocks += UnprocessedBlockBuffer_HaveBlocks;
		}

		#endregion

		#region Tracking

		/// <summary> Track a transaction </summary>
		/// <returns>False if not confirmed. True if confirmed. If too old you need to resync the chain.</returns>
		public bool Track(Transaction transaction)
		{
			// if already tracks it
			if (TrackedTransactions.Any(x=> x.GetHash() == transaction.GetHash()))
			{
				// find it 
				var tracked = TrackedTransactions.First(x => x.GetHash().Equals(transaction.GetHash()));
				// return false if not confirmed yet
				// return true if confirmed
				return tracked.Confirmed;
			}

			// if didn't track it yet add to tracked transactions
			TrackedTransactions.TryAdd(new SmartTransaction(transaction));
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
		private ConcurrentHashSet<SmartTransaction> TrackIfFindRelatedTransactions(Script scriptPubKey, int height, Block block)
		{
			var found = new ConcurrentHashSet<SmartTransaction>();
			foreach (var tx in block.Transactions)
			{
				foreach (var output in tx.Outputs)
				{
					if (output.ScriptPubKey.Equals(scriptPubKey))
					{
						TrackedTransactions.TryAdd(new SmartTransaction(tx, height));
						found.Add(new SmartTransaction(tx, height));
					}
				}
			}

			return found;
		}
		private ConcurrentHashSet<SmartTransaction> GetNotYetFoundTrackedTransactions()
		{
			var notFound = new ConcurrentHashSet<SmartTransaction>();
			foreach (var tx in TrackedTransactions.Where(x=> !x.Confirmed))
			{
				notFound.Add(tx);
			}
			return notFound;
		}

		#endregion

		public void ReorgOne()
		{
			// remove the last block
			if (MerkleChain.Count != 0)
			{
				SmartMerkleBlock bestMerkleBlock = MerkleChain.FirstOrDefault(x => x.Height == BestHeight);

				if(default(SmartMerkleBlock) != bestMerkleBlock)
				{
					MerkleChain.TryRemove(bestMerkleBlock);
				}
			}
		}

	    public void AddOrReplaceBlock(int height, Block block)
	    {
		    UnprocessedBlockBuffer.TryAddOrReplace(height, block);
	    }

	    private void ProcessBlock(int height, Block block)
		{
			ConcurrentHashSet<SmartTransaction> justFoundTransactions = new ConcurrentHashSet<SmartTransaction>();

			// 1. Look for transactions, related to our scriptpubkeys
			foreach (var spk in TrackedScriptPubKeys)
			{
				foreach(var found in TrackIfFindRelatedTransactions(spk, height, block))
				{
					justFoundTransactions.Add(found);
				}
			}

			// 2. Look for transactions, we are waiting to confirm
			ConcurrentHashSet<SmartTransaction> notYetFoundTrackedTransactions = GetNotYetFoundTrackedTransactions();
			
			foreach (var smartTransaction in notYetFoundTrackedTransactions)
			{
				if (block.Transactions.Any(x => x.GetHash().Equals(smartTransaction.GetHash())))
				{
					justFoundTransactions.Add(smartTransaction);
				}
			}

			// 3. Look for transactions, those are spending any of our transactions
			foreach(var smartTransaction in TrackedTransactions)
			{
				foreach(var tx in block.Transactions)
				{
					foreach(var input in tx.Inputs)
					{
						try
						{
							if(input.PrevOut.Hash == smartTransaction.GetHash())
							{
								justFoundTransactions.Add(smartTransaction);
							}
						}
						catch
						{
							// this tx is strange, maybe this never happens, whatever
						}
					}
				}
			}

			var trackingBlock = new SmartMerkleBlock(height, block, justFoundTransactions.Select(x=> x.GetHash()).ToArray());
			MerkleChain.Add(trackingBlock);
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
	    private const string MerkleChainFileName = "MerkleChain.dat";

		private static readonly byte[] blockSep = new byte[] { 0x10, 0x1A, 0x7B, 0x23, 0x5D, 0x12, 0x7D };
		public async Task SaveAsync(string trackerFolderPath)
		{
			await Saving.WaitAsync().ConfigureAwait(false);
			try
			{
				if (TrackedScriptPubKeys.Count > 0 || TrackedTransactions.Count > 0 || MerkleChain.Count > 0)
				{
					Directory.CreateDirectory(trackerFolderPath);
				}

				if (TrackedScriptPubKeys.Count > 0)
				{
					File.WriteAllLines(
						Path.Combine(trackerFolderPath, TrackedScriptPubKeysFileName),
						TrackedScriptPubKeys.Select(x => x.ToString()));
				}

				if (TrackedTransactions.Count > 0)
				{
					File.WriteAllLines(
						Path.Combine(trackerFolderPath, TrackedTransactionsFileName),
						TrackedTransactions.Select(x => $"{x.Transaction.ToHex()}:{x.Height}"));
				}

				if (MerkleChain.Count > 0)
				{
					var path = Path.Combine(trackerFolderPath, MerkleChainFileName);

					if(File.Exists(path))
					{
						const string backupName = MerkleChainFileName + "_backup";
						var backupPath = Path.Combine(trackerFolderPath, backupName);
						File.Copy(path, backupPath, overwrite: true);
						File.Delete(path);
					}

					using(FileStream stream = File.OpenWrite(path))
					{
						var toFile = MerkleChain.First().ToBytes();
						await stream.WriteAsync(toFile, 0, toFile.Length).ConfigureAwait(false);
						foreach(var block in MerkleChain.Skip(1))
						{
							await stream.WriteAsync(blockSep, 0, blockSep.Length).ConfigureAwait(false);
							var blockBytes = block.ToBytes();
							await stream.WriteAsync(blockBytes, 0, blockBytes.Length).ConfigureAwait(false);
						}
					}
				}
			}
			finally
			{
				Saving.Release();
			}
		}

		public async Task LoadAsync(string trackerFolderPath)
		{
			await Saving.WaitAsync().ConfigureAwait(false);
			try
			{
				if (!Directory.Exists(trackerFolderPath))
					throw new DirectoryNotFoundException($"No Blockchain found at {trackerFolderPath}");

				var tspb = Path.Combine(trackerFolderPath, TrackedScriptPubKeysFileName);
				if (File.Exists(tspb) && new FileInfo(tspb).Length != 0)
				{
					foreach (var line in File.ReadAllLines(tspb))
					{
						TrackedScriptPubKeys.Add(new Script(line));
					}
				}

				var tt = Path.Combine(trackerFolderPath, TrackedTransactionsFileName);
				if (File.Exists(tt) && new FileInfo(tt).Length != 0)
				{
					foreach (var line in File.ReadAllLines(tt))
					{
						var pieces = line.Split(':');
						TrackedTransactions.TryAdd(new SmartTransaction(new Transaction(pieces[0]), int.Parse(pieces[1])));
					}
				}

				var pbc = Path.Combine(trackerFolderPath, MerkleChainFileName);
				if (File.Exists(pbc) && new FileInfo(pbc).Length != 0)
				{
					foreach (var block in Util.Separate(File.ReadAllBytes(pbc), blockSep))
					{
						SmartMerkleBlock smartMerkleBlock = SmartMerkleBlock.FromBytes(block);
						MerkleChain.Add(smartMerkleBlock);
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
