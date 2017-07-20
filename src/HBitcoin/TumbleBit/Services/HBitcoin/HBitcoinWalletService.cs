using System;
using System.Linq;
using NBitcoin;
using Newtonsoft.Json.Linq;
using HBitcoin.KeyManagement;
using HBitcoin.FullBlockSpv;
using System.Threading.Tasks;

namespace HBitcoin.TumbleBit.Services.HBitcoin
{
	public class HBitcoinWalletService
	{
		public HBitcoinWalletService(WalletJob walletJob)
		{
			_walletJob = walletJob ?? throw new ArgumentNullException(nameof(walletJob));
		}

		private WalletJob _walletJob { get; }

		public IDestination GenerateAddress(SafeAccount inputAccount)
			=> _walletJob.GetUnusedScriptPubKeys(inputAccount, HdPathType.Change).First().GetDestinationAddress(_walletJob.Safe.Network);
		
		public async Task<Transaction> FundTransaction(SafeAccount inputAccount, TxOut txOut)
		{
			var result = await _walletJob.BuildTransactionAsync(txOut.ScriptPubKey, txOut.Value, Fees.FeeType.High, inputAccount, allowUnconfirmed: false).ConfigureAwait(false);

			if(!result.Success)
			{
				throw new InvalidOperationException(result.FailingReason);
			}
			return result.Transaction;
		}
	}
}
