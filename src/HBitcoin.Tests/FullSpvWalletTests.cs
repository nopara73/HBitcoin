using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HBitcoin.FullBlockSpv;
using HBitcoin.KeyManagement;
using NBitcoin;
using Xunit;
using Xunit.Abstractions;

namespace HBitcoin.Tests
{
    public class FullSpvWalletTests
    {
		[Fact]
		public void SycingTest()
		{
			// load wallet
			Network network = Network.Main;
			string path = $"Wallets/Empty{network}.json";
			const string password = "";
			Safe safe;
			if (File.Exists(path))
			{
				safe = Safe.Load(password, path);
				Assert.Equal(safe.Network, network);
			}
			else
			{
				Mnemonic mnemonic;
				safe = Safe.Create(out mnemonic, password, path, network);
			}

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
					System.Diagnostics.Debug.WriteLine($"{nameof(WalletJob.MaxConnectedNodeCount)} reached: {WalletJob.MaxConnectedNodeCount}");
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
				var creationHeader = WalletJob.HeaderChain.GetBlock(WalletJob.CreationHeight);
				Assert.True(creationHeader.Header.BlockTime >= Safe.EarliestPossibleCreationTime);
			}
			Assert.True(WalletJob.GetAllChainAndMemPoolTransactions().Count == 0);
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
				foreach(var addrHistory in WalletJob.SafeHistory)
				{
					Assert.True(addrHistory.Value.Count == 0);
				}
				var expectedBlockCount = WalletJob.HeaderChain.Tip.Height - WalletJob.CreationHeight + 1;
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
		    while(true)
		    {
				if (ctsToken.IsCancellationRequested) return;

				// HEADERCHAIN
				var currHeaderHeight = WalletJob.HeaderChain.Height;
				if (currHeaderHeight > _prevHeaderHeight)
				{
					System.Diagnostics.Debug.WriteLine($"HeaderChain height: {currHeaderHeight}");
					_prevHeaderHeight = currHeaderHeight;
				}

				// TRACKINGCHAIN
				var currHeight = WalletJob.BestHeight;
			    if(currHeight > _prevHeight)
				{
					System.Diagnostics.Debug.WriteLine($"TrackingChain height: {currHeight}");
					_prevHeight = currHeight;
				}

				await Task.Delay(10, ctsToken).ContinueWith(t => { }).ConfigureAwait(false);
			}
	    }
    }
}
