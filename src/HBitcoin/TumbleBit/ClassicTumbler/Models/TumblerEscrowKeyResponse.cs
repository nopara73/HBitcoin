using NBitcoin;

namespace NTumbleBit.ClassicTumbler.Models
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
