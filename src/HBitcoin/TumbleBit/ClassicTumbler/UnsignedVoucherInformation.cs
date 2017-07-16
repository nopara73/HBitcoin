using NBitcoin;

namespace HBitcoin.TumbleBit.ClassicTumbler
{
	public class UnsignedVoucherInformation
    {
		public PuzzleValue Puzzle
		{
			get; set;
		}
		public byte[] EncryptedSignature
		{
			get; set;
		}
		public uint160 Nonce
		{
			get; set;
		}
		public int CycleStart
		{
			get; set;
		}
	}
}
