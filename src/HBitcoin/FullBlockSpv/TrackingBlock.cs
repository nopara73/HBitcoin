using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace HBitcoin.FullBlockSpv
{
	public class TrackingBlock
	{
		public int Height { get; private set; }
		public MerkleBlock MerkleProof { get; private set; } = new MerkleBlock();
		public HashSet<Transaction> TrackedTransactions { get; private set; } = new HashSet<Transaction>();

		// random bytes to separate data, not very elegant
		private static readonly byte[] txSep = new byte[] { 0x30, 0x15, 0x7A, 0x29, 0x5F, 0x1D, 0x7D };
		private static readonly byte[] membSep = new byte[] { 0x3D, 0x16, 0x22, 0x3D, 0x73, 0x50, 0x1 };
		
		public TrackingBlock(int height, Block block, params uint256[] interestedTransactionIds)
		{
			Height = height;
			MerkleProof = interestedTransactionIds == null || interestedTransactionIds.Length == 0 ? block.Filter() : block.Filter(interestedTransactionIds);
			foreach(var tx in block.Transactions)
			{
				if(interestedTransactionIds.Contains(tx.GetHash()))
				{
					TrackedTransactions.Add(tx);
				}
			}
		}

		public TrackingBlock()
		{
		}

		public byte[] ToBytes()
		{
			var merkleProof = MerkleProof.ToBytes();
			byte[] transactions = null;
			if (TrackedTransactions.Count > 0)
			{
				transactions = TrackedTransactions.First().ToBytes();
				foreach (var tx in TrackedTransactions.Skip(1))
				{
					transactions = transactions.Concat(txSep).Concat(tx.ToBytes()).ToArray();
				}
			}
			var ret = BitConverter.GetBytes(Height).Concat(membSep).Concat(merkleProof).Concat(membSep).ToArray();
			if (transactions == null)
			{
				return ret;
			}
			else
			{
				return ret.Concat(transactions).ToArray();
			}
		}
		public TrackingBlock FromBytes(byte[] b)
		{
			byte[][] pieces = Util.Separate(b, membSep);

			Height = BitConverter.ToInt32(pieces[0], 0);

			// Bypass NBitcoin bug
			var emptyMerkleProofBytes = new MerkleBlock().ToBytes();
			if (emptyMerkleProofBytes.SequenceEqual(pieces[1]))
			{
				MerkleProof = new MerkleBlock();
			}
			else
			{
				MerkleProof.FromBytes(pieces[1]);
			}

			if (pieces[2].Length != 0)
			{
				foreach (byte[] tx in Util.Separate(pieces[2], txSep))
				{
					TrackedTransactions.Add(new Transaction(tx));
				}
			}

			return this;
		}
	}
}
