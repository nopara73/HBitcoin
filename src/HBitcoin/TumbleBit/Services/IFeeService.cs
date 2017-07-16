using NBitcoin;
using System;

namespace HBitcoin.TumbleBit.Services
{
	public class FeeRateUnavailableException : Exception
	{
		public FeeRateUnavailableException()
		{
		}
		public FeeRateUnavailableException(string message) : base(message) { }
		public FeeRateUnavailableException(string message, Exception inner) : base(message, inner) { }
	}
	public interface IFeeService
    {
		FeeRate GetFeeRate();
    }
}
