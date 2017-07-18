using NBitcoin;
using System.Threading.Tasks;

namespace HBitcoin.TumbleBit.Services
{
	public interface IBroadcastService
    {
		Task<bool> BroadcastAsync(Transaction tx);
		Transaction GetKnownTransaction(uint256 txId);
		Transaction[] TryBroadcast(ref uint256[] knownBroadcasted);
		Task<Transaction[]> TryBroadcastAsync();
	}
}
