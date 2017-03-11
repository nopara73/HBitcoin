using NBitcoin;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ConcurrentCollections;

namespace HBitcoin.KeyManagement
{
    public class Safe
    {
        public Network Network { get; }

        public static DateTimeOffset EarliestPossibleCreationTime { get; set; } = DateTimeOffset.ParseExact("2017-02-19", "yyyy-MM-dd", CultureInfo.InvariantCulture);

        public DateTimeOffset CreationTime { get; }

        public ExtKey ExtKey { get; private set; }

        public BitcoinExtPubKey BitcoinExtPubKey => ExtKey.Neuter().GetWif(Network);

        public BitcoinExtKey BitcoinExtKey => ExtKey.GetWif(Network);

        public string WalletFilePath { get; }

        protected Safe(string password, string walletFilePath, Network network, DateTimeOffset creationTime, Mnemonic mnemonic = null)
        {
            Network = network;
            WalletFilePath = walletFilePath;
            CreationTime = creationTime > EarliestPossibleCreationTime ? creationTime : EarliestPossibleCreationTime;

            if (mnemonic != null)
            {
                SetSeed(password, mnemonic);
            }
        }


        /// <summary>
        ///     Creates a mnemonic, a seed, encrypts it and stores in the specified path.
        /// </summary>
        /// <param name="mnemonic">empty</param>
        /// <param name="password"></param>
        /// <param name="walletFilePath"></param>
        /// <param name="network"></param>
        /// <returns>Safe</returns>
        public static Safe Create(out Mnemonic mnemonic, string password, string walletFilePath, Network network)
        {
            var creationTime = new DateTimeOffset(DateTimeOffset.UtcNow.Date);

            var safe = new Safe(password, walletFilePath, network, creationTime);

            mnemonic = safe.SetSeed(password);

            safe.Save(password, walletFilePath, network, creationTime);

            return safe;
        }

        public static Safe Load(string password, string walletFilePath)
        {
            if (!File.Exists(walletFilePath))
                throw new FileNotFoundException($"No wallet file found at {walletFilePath}");

            // deserialize the wallet
            var walletFileRawContent = WalletFileSerializer.Deserialize(walletFilePath);

            var network = walletFileRawContent.Network == Network.Main.ToString() ? Network.Main : Network.TestNet;
            var privateKey = Key.Parse(walletFileRawContent.EncryptedSeed, password, network);
            var chainCode = Convert.FromBase64String(walletFileRawContent.ChainCode);
            var seedExtKey = new ExtKey(privateKey, chainCode);
            DateTimeOffset creationTime = DateTimeOffset.ParseExact(walletFileRawContent.CreationTime, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            // initialize the safe
            return new Safe(password, walletFilePath, network, creationTime)
            {
                ExtKey = seedExtKey
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mnemonic"></param>
        /// <param name="password"></param>
        /// <param name="walletFilePath"></param>
        /// <param name="network"></param>
        /// <param name="creationTime">if null then will default to EarliestPossibleCreationTime</param>
        /// <returns></returns>
        public static Safe Recover(Mnemonic mnemonic, string password, string walletFilePath, Network network, DateTimeOffset? creationTime = null)
        {
            if (creationTime == null)
                creationTime = EarliestPossibleCreationTime;

            var safe = new Safe(password, walletFilePath, network, (DateTimeOffset)creationTime, mnemonic);
            safe.Save(password, walletFilePath, network, safe.CreationTime);
            return safe;
        }

        public BitcoinAddress GetAddress(int index, HdPathType hdPathType = HdPathType.Receive, SafeAccount account = null)
        {
            return GetPrivateKey(index, hdPathType, account).ScriptPubKey.GetDestinationAddress(Network);
        }

        public ConcurrentHashSet<BitcoinAddress> GetFirstNAddresses(int addressCount, HdPathType hdPathType = HdPathType.Receive, SafeAccount account = null)
        {
            var addresses = new ConcurrentHashSet<BitcoinAddress>();

            for (var i = 0; i < addressCount; i++)
            {
                addresses.Add(GetAddress(i, hdPathType, account));
            }

            return addresses;
        }

        // Let's generate a unique id from seedpublickey
        // Let's get the pubkey, so the chaincode is lost
        // Let's get the address, you can't directly access it from the safe
        // Also nobody would ever use this address for anythin
        /// <summary> If the wallet only differs by CreationTime, the UniqueId will be the same </summary>
        public string UniqueId => BitcoinExtPubKey.ExtPubKey.PubKey.GetAddress(Network).ToWif();
      
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
        
        public BitcoinExtKey FindPrivateKey(BitcoinAddress address, int stopSearchAfterIteration = 100000, SafeAccount account = null)
        {
            for (int i = 0; i < stopSearchAfterIteration; i++)
            {
                if (GetAddress(i, HdPathType.Receive, account) == address)
                    return GetPrivateKey(i, HdPathType.Receive, account);
                if (GetAddress(i, HdPathType.Change, account) == address)
                    return GetPrivateKey(i, HdPathType.Change, account);
                if (GetAddress(i, HdPathType.NonHardened, account) == address)
                    return GetPrivateKey(i, HdPathType.NonHardened, account);
            }

            throw new KeyNotFoundException(address.ToWif());
        }

        public BitcoinExtKey GetPrivateKey(int index, HdPathType hdPathType = HdPathType.Receive, SafeAccount account = null)
        {
            string firstPart = "";
            if (account != null)
            {
                firstPart += Hierarchy.GetPathString(account) + "/";
            }

            firstPart += Hierarchy.GetPathString(hdPathType);
            string lastPart;
            if (hdPathType == HdPathType.NonHardened)
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
            File.Delete(WalletFilePath);
        }
    }
}
