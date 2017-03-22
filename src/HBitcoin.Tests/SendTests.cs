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
			WalletJob walletJob = new WalletJob(safe, trackDefaultSafe: false, accountsToTrack: account);
			var syncedOnce = false;
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
				if(walletJob.State == WalletState.Synced)
				{
					syncedOnce = true;
				}
				else syncedOnce = false;
			};

			// start syncing
			var cts = new CancellationTokenSource();
			var walletJobTask = walletJob.StartAsync(cts.Token);
			Task reportTask = Helpers.ReportAsync(cts.Token, walletJob);

			try
			{
				// wait until fully synced
				while (!syncedOnce)
				{
					Task.Delay(1000).Wait();
				}

				var record = walletJob.GetSafeHistory(account).FirstOrDefault();
				Debug.WriteLine(record.Confirmed);
				Debug.WriteLine(record.Amount);

				var receive = walletJob.GetUnusedScriptPubKeys(account, HdPathType.Receive).FirstOrDefault();

				IDictionary<Coin, bool> unspentCoins;
				var bal = walletJob.GetBalance(out unspentCoins, account);
				Money amountToSend = (bal.Confirmed + bal.Unconfirmed) / 2;
				var res = walletJob.BuildTransactionAsync(receive, amountToSend, Models.FeeType.Low, account,
					allowUnconfirmed: true).Result;

				Assert.True(res.Success);
				Assert.True(res.FailingReason == "");
				Debug.WriteLine($"Fee: {res.Fee}");
				Debug.WriteLine($"FeePercentOfSent: {res.FeePercentOfSent} %");
				Debug.WriteLine($"SpendsUnconfirmed: {res.SpendsUnconfirmed}");
				Debug.WriteLine($"Transaction: {res.Transaction}");

				var foundReceive = false;
				Assert.InRange(res.Transaction.Outputs.Count, 1, 2);
				foreach(var output in res.Transaction.Outputs)
				{
					if(output.ScriptPubKey == receive)
					{
						foundReceive = true;
						Assert.True(amountToSend == output.Value);
					}
				}
				Assert.True(foundReceive);

				var txProbArrived = false;
				walletJob.Tracker.TrackedTransactions.CollectionChanged += delegate
				{
					txProbArrived = true;
				};
				
				var sendRes = WalletJob.SendTransactionAsync(res.Transaction).Result;
				Assert.True(sendRes.Success);
				Assert.True(sendRes.FailingReason == "");

				while (txProbArrived == false)
				{
					Debug.WriteLine("Waiting for transaction...");
					Task.Delay(1000).Wait();
				}

				Debug.WriteLine("TrackedTransactions collection changed");
				Assert.True(walletJob.Tracker.TrackedTransactions.Any(x => x.Transaction.GetHash() == res.Transaction.GetHash()));
				Debug.WriteLine("Transaction arrived");
			}
			finally
			{
				cts.Cancel();
				Task.WhenAll(reportTask, walletJobTask).Wait();
			}
		}
	}
}
