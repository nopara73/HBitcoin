using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using HBitcoin.FullBlockSpv;
using NBitcoin;
using Xunit;

namespace HBitcoin.Tests
{
	public class TrackingTests
	{
		[Fact]
		public void TrackingBlockTest()
		{
			TrackingBlock tb = new TrackingBlock(4, new MerkleBlock(Network.Main.GetGenesis(), new BloomFilter(3, 0.5)));
			var bytes = tb.ToBytes();
			var same = new TrackingBlock().FromBytes(bytes);

			Assert.Equal(tb.TrackedTransactions, same.TrackedTransactions);
			Assert.Equal(tb.Height, same.Height);
			Assert.Equal(tb.MerkleProof.Header.GetHash(), same.MerkleProof.Header.GetHash());

			// todo fix default constructor byte serialization in NBitcoin MerkleBlock
			// todo implement equality comparators for NBitcoin MerkleBlock
			// todo implement equality comparators for TrackingBlock
		}
	}
}
