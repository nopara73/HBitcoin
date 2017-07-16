using NBitcoin;

namespace HBitcoin.TumbleBit.PuzzlePromise
{
	public class SignaturesRequest
    {
		public uint256[] Hashes
		{
			get; set;
		}
		public uint256 FakeIndexesHash
		{
			get; set;
		}
	}
}
