using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HBitcoin.FullBlockSpv;
using HBitcoin.KeyManagement;
using HBitcoin.WalletDisplay;
using NBitcoin;
using Xunit;
using Xunit.Abstractions;
using System.Diagnostics;

namespace HBitcoin.Tests
{
	public class FullSpvWalletTests
	{
		[Theory]
		[InlineData("TestNet")]
		[InlineData("Main")]
		public void SycingTest(string networkString)
		{
			// load wallet
			Network network = networkString == "TestNet"? Network.TestNet:Network.Main;
			string path = $"Wallets/Empty{network}.json";
			const string password = "";
			Safe safe;
			if(File.Exists(path))
			{
				safe = Safe.Load(password, path);
				Assert.Equal(safe.Network, network);
			}
			else
			{
				Mnemonic mnemonic;
				safe = Safe.Create(out mnemonic, password, path, network);
			}

			Debug.WriteLine($"Unique Safe ID: {safe.UniqueId}");

			// create walletjob
			WalletJob.Init(safe);
			var fullyConnected = false;
			var synced = false;
			// note some event
			WalletJob.ConnectedNodeCountChanged += delegate
			{
				if(WalletJob.MaxConnectedNodeCount == WalletJob.ConnectedNodeCount)
				{
					fullyConnected = true;
					Debug.WriteLine(
						$"{nameof(WalletJob.MaxConnectedNodeCount)} reached: {WalletJob.MaxConnectedNodeCount}");
				}
				else Debug.WriteLine($"{nameof(WalletJob.ConnectedNodeCount)}: {WalletJob.ConnectedNodeCount}");
			};
			WalletJob.StateChanged += delegate
			{
				Debug.WriteLine($"{nameof(WalletJob.State)}: {WalletJob.State}");
				if(WalletJob.State == WalletState.SyncingMempool)
				{
					synced = true;
				}
			};
			Assert.True(WalletJob.SafeAccounts.Count == 0);
			Assert.True(WalletJob.ConnectedNodeCount == 0);
			if(WalletJob.CreationHeight != -1)
			{
				ChainedBlock creationHeader;
				if(WalletJob.TryGetHeader(WalletJob.CreationHeight, out creationHeader))
					Assert.True(creationHeader.Header.BlockTime >= Safe.EarliestPossibleCreationTime);
			}
			var allTxCount = WalletJob.GetAllChainAndMemPoolTransactions().Count;
			Assert.True(allTxCount == 0);
			Assert.True(!WalletJob.GetSafeHistory().Any());
			Assert.True(WalletJob.State == WalletState.NotStarted);
			Assert.True(WalletJob.TracksDefaultSafe);

			// start syncing
			var cts = new CancellationTokenSource();
			var walletJobTask = WalletJob.StartAsync(cts.Token);
			Assert.True(WalletJob.State != WalletState.NotStarted);
			Task reportTask = ReportAsync(cts.Token);

			try
			{
				// wait until fully synced and connected
				while (!fullyConnected)
				{
					Task.Delay(10).Wait();
				}

				while (!synced)
				{
					Task.Delay(1000).Wait();
				}

				Assert.True(WalletJob.State == WalletState.SyncingMempool);
				Assert.True(WalletJob.CreationHeight != -1);
				Assert.True(WalletJob.GetAllChainAndMemPoolTransactions().Count == 0);
				Assert.True(!WalletJob.GetSafeHistory().Any());
				int headerHeight;
				Assert.True(WalletJob.TryGetHeaderHeight(out headerHeight));
				var expectedBlockCount = headerHeight - WalletJob.CreationHeight + 1;
				Assert.True(WalletJob.Tracker.BlockCount == expectedBlockCount);
				Assert.True(WalletJob.Tracker.TrackedScriptPubKeys.Count > 0);
				Assert.True(WalletJob.Tracker.TrackedTransactions.Count == 0);
				Assert.True(WalletJob.Tracker.WorstHeight == WalletJob.CreationHeight);
			}
			finally
			{
				cts.Cancel();
				Task.WhenAll(reportTask, walletJobTask).Wait();
			}
		}

		private static int _prevHeight = 0;
		private static int _prevHeaderHeight = 0;

		private static async Task ReportAsync(CancellationToken ctsToken)
		{
			while (true)
			{
				if (ctsToken.IsCancellationRequested) return;
				try
				{
					await Task.Delay(1000, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);

					int currHeaderHeight;
					if(WalletJob.TryGetHeaderHeight(out currHeaderHeight))
					{
						// HEADERCHAIN
						if (currHeaderHeight > _prevHeaderHeight)
						{
							Debug.WriteLine($"HeaderChain height: {currHeaderHeight}");
							_prevHeaderHeight = currHeaderHeight;
						}

						// TRACKER
						var currHeight = WalletJob.BestHeight;
						if (currHeight > _prevHeight)
						{
							Debug.WriteLine($"Tracker height: {currHeight}");
							_prevHeight = currHeight;
						}
					}
				}
				catch
				{
					// ignored
				}
			}
		}
		
		[Fact]
		public void HaveFundsTest()
		{
			// load wallet
			Network network = Network.TestNet;
			string path = $"CommittedWallets/HaveFunds{network}.json";
			const string password = "";
			Safe safe = Safe.Load(password, path);
			Assert.Equal(safe.Network, network);
			Debug.WriteLine($"Unique Safe ID: {safe.UniqueId}");

			// create walletjob
			WalletJob.Init(safe);
			var synced = false;
			// note some event
			WalletJob.ConnectedNodeCountChanged += delegate
			{
				if(WalletJob.MaxConnectedNodeCount == WalletJob.ConnectedNodeCount)
				{
					Debug.WriteLine(
						$"{nameof(WalletJob.MaxConnectedNodeCount)} reached: {WalletJob.MaxConnectedNodeCount}");
				}
				else Debug.WriteLine($"{nameof(WalletJob.ConnectedNodeCount)}: {WalletJob.ConnectedNodeCount}");
			};
			WalletJob.StateChanged += delegate
			{
				Debug.WriteLine($"{nameof(WalletJob.State)}: {WalletJob.State}");
				if(WalletJob.State == WalletState.SyncingMempool)
				{
					synced = true;
				}
			};

			// start syncing
			var cts = new CancellationTokenSource();
			var walletJobTask = WalletJob.StartAsync(cts.Token);
			Task reportTask = ReportAsync(cts.Token);

			try
			{
				// wait until fully synced

				while(!synced)
				{
					Task.Delay(1000).Wait();
				}

				var hasMoneyAddress = BitcoinAddress.Create("mmVZjqZjmLvxc3YFhWqYWoe5anrWVcoJcc");
				Debug.WriteLine($"Checking proper balance on {hasMoneyAddress.ToWif()}");

				var record = WalletJob.GetSafeHistory().FirstOrDefault();
				Assert.True(record != default(SafeHistoryRecord));

				Assert.True(record.Confirmed);
				Assert.True(record.Amount == new Money(0.1m, MoneyUnit.BTC));
				DateTimeOffset expTime;
				DateTimeOffset.TryParse("2017.03.06. 16:47:15 +00:00", out expTime);
				Assert.True(record.TimeStamp == expTime);
				Assert.True(record.TransactionId == new uint256("50898694f281ed059fa6b9d37ccf099ab261540be14fd43ce1a6d6684fbd4e94"));
			}
			finally
			{
				cts.Cancel();
				Task.WhenAll(reportTask, walletJobTask).Wait();
			}
		}

		[Fact]
		public void RealHistoryTest()
		{
			// load wallet
			Network network = Network.TestNet;
			string path = "CommittedWallets/HiddenWallet.json";
			const string password = "";
			// I change it because I am using a very old wallet to test
			Safe.EarliestPossibleCreationTime = DateTimeOffset.ParseExact("2016-12-18", "yyyy-MM-dd", CultureInfo.InvariantCulture);
			Safe safe = Safe.Load(password, path);
			Assert.Equal(safe.Network, network);
			Debug.WriteLine($"Unique Safe ID: {safe.UniqueId}");

			// create walletjob
			WalletJob.Init(safe);
			var syncedOnce = false;
			var syncingBlocksStarted = false;
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

				if (WalletJob.State == WalletState.SyncingBlocks)
				{
					syncingBlocksStarted = true;
				}

				if (WalletJob.State == WalletState.SyncingMempool)
				{
					syncedOnce = true;
				}
			};

			// start syncing
			var cts = new CancellationTokenSource();
			var walletJobTask = WalletJob.StartAsync(cts.Token);
			Task reportTask = ReportAsync(cts.Token);

			try
			{
				while (!syncingBlocksStarted)
				{
					Task.Delay(1000).Wait();
				}
				ReportFullHistory();
				
				// wait until fully synced
				while (!syncedOnce)
				{
					Task.Delay(1000).Wait();
				}

				ReportFullHistory();
			}
			finally
			{
				cts.Cancel();
				Task.WhenAll(reportTask, walletJobTask).Wait();
			}
		}

		private static void ReportFullHistory()
		{
			var history = WalletJob.GetSafeHistory();
			if (!history.Any())
			{
				Debug.WriteLine("Wallet has no history...");
				return;
			}

			Debug.WriteLine("");
			Debug.WriteLine("---------------------------------------------------------------------------");
			Debug.WriteLine(@"Date			Amount		Confirmed	Transaction Id");
			Debug.WriteLine("---------------------------------------------------------------------------");
			
			foreach (var record in history)
			{
				Debug.WriteLine($@"{record.TimeStamp.DateTime}	{record.Amount}	{record.Confirmed}		{record.TransactionId}");
			}
			Debug.WriteLine("");
		}
	}
}
