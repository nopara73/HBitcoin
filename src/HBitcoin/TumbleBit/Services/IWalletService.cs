using NBitcoin;

namespace NTumbleBit.Services
{
	public interface IWalletService
    {
		IDestination GenerateAddress();
		Transaction FundTransaction(TxOut txOut, FeeRate feeRate);
	}
}
