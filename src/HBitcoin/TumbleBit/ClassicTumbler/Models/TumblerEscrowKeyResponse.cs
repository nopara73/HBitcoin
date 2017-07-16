using NBitcoin;

namespace HBitcoin.TumbleBit.ClassicTumbler.Models
{
	public class TumblerEscrowKeyResponse
	{
		public int KeyIndex
		{
			get; set;
		}
		public PubKey PubKey
		{
			get; set;
		}
	}
}
