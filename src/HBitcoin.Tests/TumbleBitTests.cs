using HBitcoin.FullBlockSpv;
using HBitcoin.KeyManagement;
using HBitcoin.Models;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HBitcoin.Tests
{
    public class TumbleBitTests
    {
		[Fact]
		public void CanGetTumblerParametersTest()
		{
			// load wallet
			Network network = Network.TestNet;
			string path = $"Wallets/Empty{network}.json";
			const string password = "";
			Safe safe;
			if (File.Exists(path))
			{
				safe = Safe.Load(password, path);
			}
			else
			{
				Mnemonic mnemonic;
				safe = Safe.Create(out mnemonic, password, path, network);
			}
			Debug.WriteLine($"Unique Safe ID: {safe.UniqueId}");

			// create walletjob
			WalletJob walletJob = new WalletJob(Helpers.SocksPortHandler, Helpers.ControlPortClient, safe, new Uri("http://t4cqwqlvswcyyagg.onion/api/v1/tumblers/310586435471416ca16058c1fb9ed3c868f239b9"), accountsToTrack: new SafeAccount[] { new SafeAccount(1), new SafeAccount(2)});
			
			// start syncing
			var cts = new CancellationTokenSource();
			var walletJobTask = walletJob.StartAsync(cts.Token);

			try
			{
				Assert.True(walletJob.UseTumbleBit);
				// no need to this anywhere else, because there syncing stuff is waited which automatically passes this stage
				var times = 0;
				while (walletJob.TumbleBitSetupSuccessful != true)
				{
					Task.Delay(1000).Wait();
					if(times > 21)
					{
						throw new OperationCanceledException("TumbleBit has not been setup sucessfully");
					}
					times++;
				}
				Assert.NotNull(walletJob.TumbleBitRuntime.TumblerParameters);
			}
			finally
			{
				cts.Cancel();
				Task.WhenAll(walletJobTask).Wait();
			}
		}

		[Fact]
		public void CanMixTest()
		{
			// load wallet
			Network network = Network.TestNet;
			string path = Path.Combine(Helpers.CommittedWalletsFolderPath, "RealHistoryWalletTestNet.json");
			const string password = "";
			Safe safe = Safe.Load(password, path);
			Debug.WriteLine($"Unique Safe ID: {safe.UniqueId}");

			var alice = new SafeAccount(1);
			var bob = new SafeAccount(2);
			// create walletjob
			WalletJob walletJob = new WalletJob(Helpers.SocksPortHandler, Helpers.ControlPortClient, safe, new Uri("http://t4cqwqlvswcyyagg.onion/api/v1/tumblers/310586435471416ca16058c1fb9ed3c868f239b9"), trackDefaultSafe:false, accountsToTrack: new SafeAccount[] { alice, bob });
			
			// note some event
			WalletJob.ConnectedNodeCountChanged += delegate
			{
				if (WalletJob.MaxConnectedNodeCount == WalletJob.ConnectedNodeCount)
				{
					Debug.WriteLine(
						$"{nameof(WalletJob.MaxConnectedNodeCount)} reached: {WalletJob.MaxConnectedNodeCount}");
				}
				else Debug.WriteLine($"{nameof(WalletJob.ConnectedNodeCount)}: {WalletJob.ConnectedNodeCount}");
			};
			walletJob.StateChanged += delegate
			{
				Debug.WriteLine($"{nameof(walletJob.State)}: {walletJob.State}");
			};

			// start syncing
			var cts = new CancellationTokenSource();
			var walletJobTask = walletJob.StartAsync(cts.Token);
			Task reportTask = Helpers.ReportAsync(cts.Token, walletJob);
			try
			{
				// wait until blocks are synced
				while (walletJob.State < WalletState.SyncingMemPool)
				{
					Task.Delay(100).Wait();
				}

				Debug.WriteLine("Alice balance: " + walletJob.GetBalance(out IDictionary<Coin, bool> unspentCoinsA, alice).Confirmed.ToDecimal(MoneyUnit.BTC));
				Debug.WriteLine("Bob balance: " + walletJob.GetBalance(out IDictionary<Coin, bool> unspentCoinsB, bob).Confirmed.ToDecimal(MoneyUnit.BTC));

				walletJob.TumbleBitBroadcaster.Start(alice, bob);
				Task.Delay(TimeSpan.FromMinutes(1));
			}
			finally
			{
				if (walletJob.TumbleBitSetupSuccessful && walletJob.TumbleBitBroadcaster.Started)
				{
					walletJob.TumbleBitBroadcaster.Stop();
				}
				cts.Cancel();
				Task.WhenAll(walletJobTask, reportTask).Wait();
			}
		}
	}
}
