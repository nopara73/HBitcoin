using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HBitcoin.FullBlockSpv;
using HBitcoin.KeyManagement;
using NBitcoin;
using Xunit;

namespace HBitcoin.Tests
{
	public class SendTests
	{
		[Fact]
		public void BasicSendTest()
		{
			Network network = Network.TestNet;
			SafeAccount account = new SafeAccount(1);
			string path = $"CommittedWallets/Sending{network}.json";
			const string password = "";
			Safe safe = Safe.Load(password, path);
			Assert.Equal(safe.Network, network);
			Debug.WriteLine($"Unique Safe ID: {safe.UniqueId}");

			// create walletjob
			WalletJob.Init(safe, trackDefaultSafe: false, accountsToTrack: account);
			var synced = false;
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
			WalletJob.StateChanged += delegate
			{
				Debug.WriteLine($"{nameof(WalletJob.State)}: {WalletJob.State}");
				if(WalletJob.State == WalletState.Synced)
				{
					synced = true;
				}
				else synced = false;
			};

			// start syncing
			var cts = new CancellationTokenSource();
			var walletJobTask = WalletJob.StartAsync(cts.Token);
			Task reportTask = Helpers.ReportAsync(cts.Token);

			try
			{
				// wait until fully synced
				while (!synced)
				{
					Task.Delay(1000).Wait();
				}

				var record = WalletJob.GetSafeHistory(account).FirstOrDefault();
				Debug.WriteLine(record.Confirmed);
				Debug.WriteLine(record.Amount);
			}
			finally
			{
				cts.Cancel();
				Task.WhenAll(reportTask, walletJobTask).Wait();
			}
		}
	}
}
