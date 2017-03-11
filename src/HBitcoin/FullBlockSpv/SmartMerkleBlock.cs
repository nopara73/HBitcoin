using System;
using System.Collections.Generic;
using System.Linq;
using ConcurrentCollections;
using NBitcoin;

namespace HBitcoin.FullBlockSpv
{
	public class SmartMerkleBlock
	{
		#region Members
		
		public int Height { get; }
		public MerkleBlock MerkleBlock { get; }

		public IEnumerable<uint256> GetMatchedTransactions() => MerkleBlock.PartialMerkleTree.GetMatchedTransactions();
		public uint TransactionCount => MerkleBlock.PartialMerkleTree.TransactionCount;

		#endregion

		#region Constructors

		public SmartMerkleBlock()
		{

		}

		public SmartMerkleBlock(int height, Block block, params uint256[] interestedTransactionIds)
		{
			Height = height;
			MerkleBlock = interestedTransactionIds == null || interestedTransactionIds.Length == 0 ? block.Filter() : block.Filter(interestedTransactionIds);
		}

		public SmartMerkleBlock(int height, MerkleBlock merkleBlock)
		{
			Height = height;
			MerkleBlock = merkleBlock;
		}

		#endregion

		#region Formatting

		public static byte[] ToBytes(SmartMerkleBlock smartMerkleBlock) => 
			BitConverter.GetBytes(smartMerkleBlock.Height) // 4bytes
			.Concat(smartMerkleBlock.MerkleBlock.ToBytes())
			.ToArray();

		public byte[] ToBytes() => ToBytes(this);

		public static SmartMerkleBlock FromBytes(byte[] bytes)
		{
			var heightBytes = bytes.Take(4).ToArray();
			var merkleBlockBytes = bytes.Skip(4).ToArray();

			int height = BitConverter.ToInt32(heightBytes, startIndex: 0);

			// Bypass NBitcoin bug
			var merkleBlock = new MerkleBlock();
			if(!merkleBlock.ToBytes().SequenceEqual(merkleBlockBytes)) // if not default MerkleBlock
			{
				merkleBlock.FromBytes(merkleBlockBytes);
			}

			return new SmartMerkleBlock(height, merkleBlock);
		}

		#endregion
	}
}
