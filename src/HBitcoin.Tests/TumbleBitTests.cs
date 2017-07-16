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
			WalletJob walletJob = new WalletJob(Helpers.SocksPortHandler, Helpers.ControlPortClient, safe);
			
			// start syncing
			var cts = new CancellationTokenSource();
			var walletJobTask = walletJob.StartAsync(cts.Token);

			try
			{
				Debug.WriteLine(walletJob.GetTumblerInfoAsync(new Uri("http://t4cqwqlvswcyyagg.onion/api/v1/tumblers/310586435471416ca16058c1fb9ed3c868f239b9")).Result);
			}
			finally
			{
				cts.Cancel();
				Task.WhenAll(walletJobTask).Wait();
			}
		}
	}
}
