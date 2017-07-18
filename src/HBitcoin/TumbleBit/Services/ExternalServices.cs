using NBitcoin.RPC;
using HBitcoin.TumbleBit.Services.HBitcoin;
using HBitcoin.FullBlockSpv;

namespace HBitcoin.TumbleBit.Services
{
	public class ExternalServices
    {
		public static ExternalServices CreateFromRPCClient(RPCClient rpc, WalletJob walletJob, IRepository repository, Tracker tracker)
		{
			var service = new ExternalServices();

			var cache = new HBitcoinWalletCache(walletJob, repository);
			service.WalletService = new RPCWalletService(rpc);
			service.BroadcastService = new HBitcoinBroadcastService(walletJob, cache, repository);
			service.BlockExplorerService = new HBitcoinBlockExplorerService(walletJob, cache, repository);
			service.TrustedBroadcastService = new HBitcoinTrustedBroadcastService(walletJob, service.BroadcastService, service.BlockExplorerService, repository, cache, tracker)
			{
				//BlockExplorer will already track the addresses, since they used a shared bitcoind, no need of tracking again (this would overwrite labels)
				TrackPreviousScriptPubKey = false
			};
			return service;
		}

		public IWalletService WalletService { get; set; }
		public IBroadcastService BroadcastService { get; set; }
		public IBlockExplorerService BlockExplorerService { get; set; }
		public ITrustedBroadcastService TrustedBroadcastService { get; set; }
	}
}
