using NBitcoin;

namespace HBitcoin.TumbleBit.Services
{
	public interface IBroadcastService
    {
		bool Broadcast(Transaction tx);
		Transaction GetKnownTransaction(uint256 txId);
		Transaction[] TryBroadcast(ref uint256[] knownBroadcasted);
		Transaction[] TryBroadcast();
	}
}
