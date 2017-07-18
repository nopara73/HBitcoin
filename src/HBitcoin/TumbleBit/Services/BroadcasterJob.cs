using HBitcoin.KeyManagement;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HBitcoin.TumbleBit.Services
{
	public class BroadcasterJob : TumblerServiceBase
	{
		public BroadcasterJob(ExternalServices services)
		{
			BroadcasterService = services.BroadcastService;
			TrustedBroadcasterService = services.TrustedBroadcastService;
			BlockExplorerService = services.BlockExplorerService;
		}

		public IBroadcastService BroadcasterService { get; private set; }
		public ITrustedBroadcastService TrustedBroadcasterService { get; private set; }
		public IBlockExplorerService BlockExplorerService { get; private set; }
		public override string Name => "broadcaster";

		public Transaction[] TryBroadcast()
		{
			uint256[] knownBroadcasted = null;
			var broadcasted = new List<Transaction>();
			try
			{
				broadcasted.AddRange(BroadcasterService.TryBroadcast(ref knownBroadcasted));
			}
			catch(Exception ex)
			{
				Debug.WriteLine("ERROR: Exception on Broadcaster");
				Debug.WriteLine("ERROR: " + ex.ToString());
			}
			try
			{
				broadcasted.AddRange(TrustedBroadcasterService.TryBroadcast(ref knownBroadcasted));
			}
			catch(Exception ex)
			{
				Debug.WriteLine("ERROR: Exception on TrustedBroadcaster");
				Debug.WriteLine("ERROR: " + ex.ToString());
			}
			return broadcasted.ToArray();
		}

		protected override void StartCore(CancellationToken cancellationToken, SafeAccount outputAccount)
		{
			Task.Run(async () =>
			{
				Debug.WriteLine("BroadcasterJob started");
				while(true)
				{
					Exception unhandled = null;
					try
					{
						var lastBlock = uint256.Zero;
						while (true)
						{
							lastBlock = await BlockExplorerService.WaitBlockAsync(lastBlock, cancellationToken).ConfigureAwait(false);
							TryBroadcast();
						}
					}
					catch(OperationCanceledException ex)
					{
						if(cancellationToken.IsCancellationRequested)
						{
							Stopped();
							break;
						}
						else
							unhandled = ex;
					}
					catch(Exception ex)
					{
						unhandled = ex;
					}
					if(unhandled != null)
					{
						Debug.WriteLine("ERROR: Uncaught exception BroadcasterJob : " + unhandled.ToString());
						await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
					}
				}
			});
		}
	}
}
