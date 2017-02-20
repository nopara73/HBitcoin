using NBitcoin;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HBitcoin.KeyManagement
{
	public class Safe
	{
		public Network Network { get; }
		
		public static DateTimeOffset EarliestPossibleCreationTime
			=> DateTimeOffset.ParseExact("2017-02-19", "yyyy-MM-dd", CultureInfo.InvariantCulture);
		public DateTimeOffset CreationTime { get; }

		public ExtKey ExtKey { get; private set; }
		public BitcoinExtPubKey BitcoinExtPubKey => ExtKey.Neuter().GetWif(Network);
		public BitcoinExtKey BitcoinExtKey => ExtKey.GetWif(Network);

		public BitcoinAddress GetAddress(int index, HdPathType hdPathType = HdPathType.Receive)
		{
			return GetPrivateKey(index, hdPathType).ScriptPubKey.GetDestinationAddress(Network);
		}

		public HashSet<BitcoinAddress> GetFirstNAddresses(int addressCount, HdPathType hdPathType = HdPathType.Receive)
		{
			var addresses = new HashSet<BitcoinAddress>();

			for (var i = 0; i < addressCount; i++)
			{
				addresses.Add(GetAddress(i, hdPathType));
			}

			return addresses;
		}

		// Let's generate a unique id from seedpublickey
		// Let's get the pubkey, so the chaincode is lost
		// Let's get the address, you can't directly access it from the safe
		// Also nobody would ever use this address for anythin
		/// <summary> If the wallet only differs by CreationTime, the UniqueId will be the same </summary>
		public string UniqueId => BitcoinExtPubKey.ExtPubKey.PubKey.GetAddress(Network).ToWif();

		private string WalletName { get; }
		private static string GenerateWalletFilePath(string walletName) => Path.Combine("Wallets", $"{walletName}.json");
		public string WalletFilePath => GenerateWalletFilePath(WalletName);

		protected Safe(string password, string walletName, Network network, DateTimeOffset creationTime, Mnemonic mnemonic = null)
		{
			Network = network;
			CreationTime = creationTime > EarliestPossibleCreationTime ? creationTime : EarliestPossibleCreationTime;

			if (mnemonic != null)
			{
				SetSeed(password, mnemonic);
			}

			WalletName = walletName;
		}

		public Safe(Safe safe)
		{
			Network = safe.Network;
			CreationTime = safe.CreationTime;
			ExtKey = safe.ExtKey;
			WalletName = safe.WalletName;
		}

		/// <summary>
		///     Creates a mnemonic, a seed, encrypts it and stores in the specified path.
		/// </summary>
		/// <param name="mnemonic">empty</param>
		/// <param name="password"></param>
		/// <param name="walletName"></param>
		/// <param name="network"></param>
		/// <returns>Safe</returns>
		public static Safe Create(out Mnemonic mnemonic, string password, string walletName, Network network)
		{
			var creationTime = new DateTimeOffset(DateTimeOffset.UtcNow.Date);

			var safe = new Safe(password, walletName, network, creationTime);

			mnemonic = safe.SetSeed(password);

			safe.Save(password, GenerateWalletFilePath(walletName), network, creationTime);

			return safe;
		}

		public static Safe Recover(Mnemonic mnemonic, string password, string walletName, Network network, DateTimeOffset creationTime)
		{
			var safe = new Safe(password, walletName, network, creationTime, mnemonic);
			safe.Save(password, GenerateWalletFilePath(walletName), network, safe.CreationTime);
			return safe;
		}

		private Mnemonic SetSeed(string password, Mnemonic mnemonic = null)
		{
			mnemonic = mnemonic ?? new Mnemonic(Wordlist.English, WordCount.Twelve);

			ExtKey = mnemonic.DeriveExtKey(password);

			return mnemonic;
		}

		private void SetSeed(ExtKey seedExtKey) => ExtKey = seedExtKey;

		private void Save(string password, string walletFilePath, Network network, DateTimeOffset creationTime)
		{
			if (File.Exists(walletFilePath))
				throw new NotSupportedException($"Wallet already exists at {walletFilePath}");

			var directoryPath = Path.GetDirectoryName(Path.GetFullPath(walletFilePath));
			if (directoryPath != null) Directory.CreateDirectory(directoryPath);

			var privateKey = ExtKey.PrivateKey;
			var chainCode = ExtKey.ChainCode;

			var encryptedBitcoinPrivateKeyString = privateKey.GetEncryptedBitcoinSecret(password, Network).ToWif();
			var chainCodeString = Convert.ToBase64String(chainCode);

			var networkString = network.ToString();

			var creationTimeString = creationTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

			WalletFileSerializer.Serialize(
				walletFilePath,
				encryptedBitcoinPrivateKeyString,
				chainCodeString,
				networkString,
				creationTimeString);
		}

		public static Safe Load(string password, string walletName)
		{
			var walletFilePath = GenerateWalletFilePath(walletName);
			if (!File.Exists(walletFilePath))
				throw new ArgumentException($"No wallet file found at {walletFilePath}");

			var walletFileRawContent = WalletFileSerializer.Deserialize(walletFilePath);

			var encryptedBitcoinPrivateKeyString = walletFileRawContent.EncryptedSeed;
			var chainCodeString = walletFileRawContent.ChainCode;

			var chainCode = Convert.FromBase64String(chainCodeString);

			Network network;
			var networkString = walletFileRawContent.Network;
			network = networkString == Network.Main.ToString() ? Network.Main : Network.TestNet;

			DateTimeOffset creationTime = DateTimeOffset.ParseExact(walletFileRawContent.CreationTime, "yyyy-MM-dd", CultureInfo.InvariantCulture);

			var safe = new Safe(password, walletName, network, creationTime);

			var privateKey = Key.Parse(encryptedBitcoinPrivateKeyString, password, safe.Network);
			var seedExtKey = new ExtKey(privateKey, chainCode);
			safe.SetSeed(seedExtKey);

			return safe;
		}

		public BitcoinExtKey FindPrivateKey(BitcoinAddress address, int stopSearchAfterIteration = 100000)
		{
			for (int i = 0; i < stopSearchAfterIteration; i++)
			{
				if (GetAddress(i, HdPathType.Receive) == address)
					return GetPrivateKey(i, HdPathType.Receive);
				if (GetAddress(i, HdPathType.Change) == address)
					return GetPrivateKey(i, HdPathType.Change);
				if (GetAddress(i, HdPathType.NonHardened) == address)
					return GetPrivateKey(i, HdPathType.NonHardened);
			}

			throw new KeyNotFoundException(address.ToWif());
		}

		public BitcoinExtKey GetPrivateKey(int index, HdPathType hdPathType = HdPathType.Receive)
		{
			string firstPart = Hierarchy.GetPathString(hdPathType);
			string lastPart;
			if(hdPathType == HdPathType.NonHardened)
			{
				lastPart = $"/{index}";
			}
			else lastPart = $"/{index}'";

			KeyPath keyPath = new KeyPath(firstPart + lastPart);

			return ExtKey.Derive(keyPath).GetWif(Network);
		}

		public string GetCreationTimeString()
		{
			return CreationTime.ToString("s", CultureInfo.InvariantCulture);
		}

		public void Delete()
		{
			if(File.Exists(WalletFilePath))
				File.Delete(WalletFilePath);
		}
	}
}
