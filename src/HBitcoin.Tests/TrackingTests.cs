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
			TrackingBlock tb = new TrackingBlock(4, Network.Main.GetGenesis());
			var bytes = tb.ToBytes();
			var same = new TrackingBlock().FromBytes(bytes);

			Assert.Equal(tb.TrackedTransactions.Count, same.TrackedTransactions.Count);
			Assert.Equal(tb.Height, same.Height);
			Assert.Equal(tb.MerkleProof.Header.GetHash(), same.MerkleProof.Header.GetHash());

			// todo fix default constructor byte serialization in NBitcoin MerkleBlock
			// todo implement equality comparators for NBitcoin MerkleBlock
			// todo implement equality comparators for TrackingBlock

			var block = Network.Main.GetGenesis();
			var tx = new Transaction(
				"0100000001997ae2a654ddb2432ea2fece72bc71d3dbd371703a0479592efae21bf6b7d5100100000000ffffffff01e00f9700000000001976a9142a495afa8b8147ec2f01713b18693cb0a85743b288ac00000000");
			block.AddTransaction(tx);
			var tb2 = new TrackingBlock(1, block, tx.GetHash());
			tb2.TrackedTransactions.Add(tx);
			var bytes2 = tb2.ToBytes();
			var same2 = new TrackingBlock().FromBytes(bytes2);

			Assert.Equal(1, same2.TrackedTransactions.Count);
			Assert.Equal(tb2.Height, same2.Height);
			Assert.Equal(tb2.MerkleProof.Header.GetHash(), same2.MerkleProof.Header.GetHash());
			var txid1 = tb2.TrackedTransactions.FirstOrDefault().GetHash();
			var txid2 = same2.TrackedTransactions.FirstOrDefault().GetHash();
			Assert.Equal(txid1, txid2);
		}
	}
}
