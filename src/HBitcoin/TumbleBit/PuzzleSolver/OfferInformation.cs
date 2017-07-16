using NBitcoin;

namespace HBitcoin.TumbleBit.PuzzleSolver
{
	public class OfferInformation
    {
		public Money Fee
		{
			get;
			set;
		}
		public LockTime LockTime
		{
			get; set;
		}
		public PubKey FulfillKey
		{
			get; set;
		}
	}
}
