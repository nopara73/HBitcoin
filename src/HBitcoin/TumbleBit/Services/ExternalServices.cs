using HBitcoin.TumbleBit.Services.HBitcoin;
using HBitcoin.FullBlockSpv;
using HBitcoin.KeyManagement;

namespace HBitcoin.TumbleBit.Services
{
	public class ExternalServices
    {
		public static ExternalServices CreateFromHBitcoinClient(WalletJob walletJob, IRepository repository, Tracker tracker)
		{
			var service = new ExternalServices();

			var cache = new HBitcoinWalletCache(walletJob, repository);
			service.WalletService = new HBitcoinWalletService(walletJob);
			service.BroadcastService = new HBitcoinBroadcastService(walletJob, cache, repository);
			service.BlockExplorerService = new HBitcoinBlockExplorerService(walletJob, cache, repository);
			service.TrustedBroadcastService = new HBitcoinTrustedBroadcastService(walletJob, service.BroadcastService, service.BlockExplorerService, repository, cache, tracker)
			{
				//BlockExplorer will already track the addresses, since they used a shared bitcoind, no need of tracking again (this would overwrite labels)
				TrackPreviousScriptPubKey = false
			};
			return service;
		}

		public HBitcoinWalletService WalletService { get; set; }
		public IBroadcastService BroadcastService { get; set; }
		public IBlockExplorerService BlockExplorerService { get; set; }
		public ITrustedBroadcastService TrustedBroadcastService { get; set; }
	}
}
