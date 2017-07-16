using NBitcoin;

namespace NTumbleBit.PuzzleSolver
{
	public class CashoutInformation
    {
		public Script Cashout
		{
			get; set;
		}
		public Money Fee
		{
			get; set;
		}
	}
}
