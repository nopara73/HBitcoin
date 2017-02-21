using System;
using System.Globalization;
using HBitcoin.KeyManagement;
using NBitcoin;
using Xunit;

namespace HBitcoin.Tests
{
	public class SafeTests
	{
		[Fact]
		public void CreationTests()
		{
			for(int i = 0; i < 2; i++)
			{
				var network = i == 0 ? Network.Main : Network.TestNet;

				Mnemonic mnemonic;
				const string path = "Wallets/TestWallet.json";
				const string password = "password";

				var safe = Safe.Create(out mnemonic, password, path, network);
				var loadedSafe = Safe.Load(password, path);

				var wantedCreation = DateTimeOffset.ParseExact("1998-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
				var recoverdSafe = Safe.Recover(mnemonic, password, "Wallets/RecoveredTestWallet.json", network, wantedCreation);

				try
				{
					Assert.Equal(DateTimeOffset.UtcNow.Date, safe.CreationTime.Date);
					Assert.True(Safe.EarliestPossibleCreationTime < safe.CreationTime);
					Assert.True(wantedCreation < recoverdSafe.CreationTime);
					Assert.Equal(network, safe.Network);
					Assert.Equal(network, loadedSafe.Network);
					Assert.Equal(network, recoverdSafe.Network);
				}
				finally
				{
					safe.Delete();
					recoverdSafe.Delete();
				}
			}
		}

		[Theory]
		[InlineData(0)]
		[InlineData(1)]
		[InlineData(100)]
		[InlineData(9999)]
		public void ProperlyLoadRecover(int index)
		{
			Network network = Network.TestNet;
			Mnemonic mnemonic;
			const string path = "Wallets/TestWallet2.json";
			const string password = "password";

			var safe = Safe.Create(out mnemonic, password, path, network);
			var loadedSafe = Safe.Load(password, path);
			var recoverdSafe = Safe.Recover(mnemonic, password, "Wallets/RecoveredTestWallet.json", network, Safe.EarliestPossibleCreationTime);

			try
			{
				Assert.Equal(safe.ExtKey.ScriptPubKey, loadedSafe.ExtKey.ScriptPubKey);
				Assert.Equal(safe.ExtKey.ScriptPubKey, loadedSafe.ExtKey.ScriptPubKey);
				Assert.Equal(loadedSafe.BitcoinExtKey, recoverdSafe.BitcoinExtKey);
				Assert.Equal(loadedSafe.BitcoinExtPubKey, recoverdSafe.BitcoinExtPubKey);
				Assert.Equal(loadedSafe.GetAddress(index), recoverdSafe.GetAddress(index));
				Assert.Equal(loadedSafe.GetPrivateKey(index), recoverdSafe.GetPrivateKey(index));
			}
			finally
			{
				safe.Delete();
				recoverdSafe.Delete();
			}
		}
	}
}
