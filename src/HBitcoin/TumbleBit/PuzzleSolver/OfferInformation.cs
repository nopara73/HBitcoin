using NBitcoin;

namespace NTumbleBit.PuzzleSolver
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
