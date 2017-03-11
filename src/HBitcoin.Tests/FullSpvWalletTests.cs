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

namespace HBitcoin.Tests
{
	public class FullSpvWalletTests
	{
		[Theory]
		[InlineData("TestNet")]
		[InlineData("Main")]
		public void SyncingTest(string networkString)
		{
            // load wallet
            Network network = networkString == "TestNet" ? Network.TestNet : Network.Main;
            Safe safe = Safe.Load(string.Empty, $"../../Wallets/Empty{network}.json");
            Assert.Equal(network, safe.Network);
            
			System.Diagnostics.Debug.WriteLine($"Unique Safe ID: {safe.UniqueId}");

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
					System.Diagnostics.Debug.WriteLine(
						$"{nameof(WalletJob.MaxConnectedNodeCount)} reached: {WalletJob.MaxConnectedNodeCount}");
				}
				else System.Diagnostics.Debug.WriteLine($"{nameof(WalletJob.ConnectedNodeCount)}: {WalletJob.ConnectedNodeCount}");
			};
			WalletJob.StateChanged += delegate
			{
				System.Diagnostics.Debug.WriteLine($"{nameof(WalletJob.State)}: {WalletJob.State}");
				if(WalletJob.State == WalletState.SyncingMempool)
				{
					synced = true;
				}
			};
			Assert.True(WalletJob.Accounts.Count == 0);
			Assert.True(WalletJob.ConnectedNodeCount == 0);
			if(WalletJob.CreationHeight != -1)
			{
				ChainedBlock creationHeader;
				if(WalletJob.TryGetHeader(WalletJob.CreationHeight, out creationHeader))
					Assert.True(creationHeader.Header.BlockTime >= Safe.EarliestPossibleCreationTime);
			}
			var allTxCount = WalletJob.GetAllChainAndMemPoolTransactions().Count;
			Assert.True(allTxCount == 0);
			Assert.True(WalletJob.SafeHistory.Count == 0);
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
				foreach (var addrHistory in WalletJob.SafeHistory)
				{
					Assert.True(addrHistory.Value.Count == 0);
				}
				int headerHeight;
				Assert.True(WalletJob.TryGetHeaderHeight(out headerHeight));
				var expectedBlockCount = headerHeight - WalletJob.CreationHeight + 1;
				Assert.True(WalletJob.TrackingChain.BlockCount == expectedBlockCount);
				Assert.True(WalletJob.TrackingChain.TrackedScriptPubKeys.Count > 0);
				Assert.True(WalletJob.TrackingChain.TrackedTransactions.Count == 0);
				Assert.True(WalletJob.TrackingChain.WorstHeight == WalletJob.CreationHeight);
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
							System.Diagnostics.Debug.WriteLine($"HeaderChain height: {currHeaderHeight}");
							_prevHeaderHeight = currHeaderHeight;
						}

						// TRACKINGCHAIN
						var currHeight = WalletJob.BestHeight;
						if (currHeight > _prevHeight)
						{
							System.Diagnostics.Debug.WriteLine($"TrackingChain height: {currHeight}");
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
            Safe safe = Safe.Load(string.Empty, $"../../CommittedWallets/HaveFunds{network}.json");
            Assert.Equal(network, safe.Network);

            System.Diagnostics.Debug.WriteLine($"Unique Safe ID: {safe.UniqueId}");

			// create walletjob
			WalletJob.Init(safe);
			var synced = false;
			// note some event
			WalletJob.ConnectedNodeCountChanged += delegate
			{
				if(WalletJob.MaxConnectedNodeCount == WalletJob.ConnectedNodeCount)
				{
					System.Diagnostics.Debug.WriteLine(
						$"{nameof(WalletJob.MaxConnectedNodeCount)} reached: {WalletJob.MaxConnectedNodeCount}");
				}
				else System.Diagnostics.Debug.WriteLine($"{nameof(WalletJob.ConnectedNodeCount)}: {WalletJob.ConnectedNodeCount}");
			};
			WalletJob.StateChanged += delegate
			{
				System.Diagnostics.Debug.WriteLine($"{nameof(WalletJob.State)}: {WalletJob.State}");
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

				while (!synced)
				{
					Task.Delay(1000).Wait();
				}
				BitcoinAddress firstReceive = null;
				foreach(KeyValuePair<Script, ObservableCollection<ScriptPubKeyHistoryRecord>> scriptPubKey in WalletJob.SafeHistory)
				{
					if(scriptPubKey.Value.Count == 0)
					{
						firstReceive = scriptPubKey.Key.GetDestinationAddress(WalletJob.Network);
						break;
					}
				}
				System.Diagnostics.Debug.WriteLine($"First unused receive address: {firstReceive.ToWif()}");
				var hasMoneyAddress = BitcoinAddress.Create("mmVZjqZjmLvxc3YFhWqYWoe5anrWVcoJcc");
				Assert.True(firstReceive.ToWif() != hasMoneyAddress.ToWif());

				foreach (KeyValuePair<Script, ObservableCollection<ScriptPubKeyHistoryRecord>> scriptPubKey in WalletJob.SafeHistory)
				{
					if (scriptPubKey.Key == hasMoneyAddress.ScriptPubKey)
					{
						var record = scriptPubKey.Value.FirstOrDefault();
						Assert.True(record != default(ScriptPubKeyHistoryRecord));

						Assert.True(record.Confirmation > 0);
						Assert.True(record.Amount == new Money(0.1m, MoneyUnit.BTC));
						DateTimeOffset expTime;
						DateTimeOffset.TryParse("2017.03.06. 16:47:15 +00:00", out expTime);
						Assert.True(record.TimeStamp == expTime);
						Assert.True(record.TransactionId ==
									new uint256("50898694f281ed059fa6b9d37ccf099ab261540be14fd43ce1a6d6684fbd4e94"));
						break;
					}
				}
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
            Network network = Network.TestNet;
            
            // I change it because I am using a very old wallet to test
			Safe.EarliestPossibleCreationTime = DateTimeOffset.ParseExact("2016-12-18", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            Safe safe = Safe.Load(string.Empty, $"../../CommittedWallets/HiddenWallet.json");
            Assert.Equal(network, safe.Network);
			System.Diagnostics.Debug.WriteLine($"Unique Safe ID: {safe.UniqueId}");

			// create walletjob
			WalletJob.Init(safe);
			var synced = false;
			// note some event
			WalletJob.ConnectedNodeCountChanged += delegate
			{
				if (WalletJob.MaxConnectedNodeCount == WalletJob.ConnectedNodeCount)
				{
					System.Diagnostics.Debug.WriteLine(
						$"{nameof(WalletJob.MaxConnectedNodeCount)} reached: {WalletJob.MaxConnectedNodeCount}");
				}
				else System.Diagnostics.Debug.WriteLine($"{nameof(WalletJob.ConnectedNodeCount)}: {WalletJob.ConnectedNodeCount}");
			};
			WalletJob.StateChanged += delegate
			{
				System.Diagnostics.Debug.WriteLine($"{nameof(WalletJob.State)}: {WalletJob.State}");
				if (WalletJob.State == WalletState.SyncingMempool)
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

				while (!synced)
				{
					Task.Delay(1000).Wait();
				}

				System.Diagnostics.Debug.WriteLine("");
				System.Diagnostics.Debug.WriteLine("---------------------------------------------------------------------------");
				System.Diagnostics.Debug.WriteLine(@"Date			Amount		Confirmed	Transaction Id");
				System.Diagnostics.Debug.WriteLine("---------------------------------------------------------------------------");
				List<ScriptPubKeyHistoryRecord> records = new List<ScriptPubKeyHistoryRecord>();

				foreach (KeyValuePair<Script, ObservableCollection<ScriptPubKeyHistoryRecord>> scriptPubKeyHistory in WalletJob.SafeHistory)
				{
					if (scriptPubKeyHistory.Value.Count > 0)
					{
						foreach(var record in scriptPubKeyHistory.Value)
						{
							records.Add(record);
						}
					}
				}
				foreach(var record in records.OrderBy(x => x.TimeStamp))
				{
					System.Diagnostics.Debug.WriteLine($@"{record.TimeStamp.DateTime}	{record.Amount}	{record.Confirmation > 0}		{record.TransactionId}");
				}

				System.Diagnostics.Debug.WriteLine("");
			}
			finally
			{
				cts.Cancel();
				Task.WhenAll(reportTask, walletJobTask).Wait();
			}
		}
	}
}
