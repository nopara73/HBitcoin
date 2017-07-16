using NBitcoin;

namespace HBitcoin.TumbleBit.Services
{
	public interface IWalletService
    {
		IDestination GenerateAddress();
		Transaction FundTransaction(TxOut txOut, FeeRate feeRate);
	}
}
