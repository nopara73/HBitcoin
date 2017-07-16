using System;
using NBitcoin;
using NBitcoin.RPC;

namespace HBitcoin.TumbleBit.Services.RPC
{
	public class RPCFeeService : IFeeService
	{
		public RPCFeeService(RPCClient rpc)
		{
			_RPCClient = rpc ?? throw new ArgumentNullException(nameof(rpc));
		}

		private readonly RPCClient _RPCClient;
		public RPCClient RPCClient => _RPCClient;

		public FeeRate FallBackFeeRate
		{
			get; set;
		}
		public FeeRate MinimumFeeRate
		{
			get; set;
		}
		public FeeRate GetFeeRate()
		{
			var rate = _RPCClient.TryEstimateFeeRate(1) ??
				   _RPCClient.TryEstimateFeeRate(2) ??
				   _RPCClient.TryEstimateFeeRate(3) ??
				   FallBackFeeRate;
			if(rate == null)
				throw new FeeRateUnavailableException("The fee rate is unavailable");
			if(rate < MinimumFeeRate)
				rate = MinimumFeeRate;
			return rate;
		}
	}
}
