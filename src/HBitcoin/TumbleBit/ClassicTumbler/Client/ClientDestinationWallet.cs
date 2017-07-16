using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using HBitcoin.TumbleBit.Services;
using System;
using System.Diagnostics;
using System.Text;

namespace HBitcoin.TumbleBit.ClassicTumbler.Client
{
	public interface IDestinationWallet
	{
		Script GetNewDestination();
		KeyPath GetKeyPath(Script script);
	}
	public class ClientDestinationWallet : IDestinationWallet
	{
		ExtPubKey _ExtPubKey;
		KeyPath _DerivationPath;
		Network _Network;

		public ClientDestinationWallet(BitcoinExtPubKey extPubKey, KeyPath derivationPath, IRepository repository, Network network)
		{
			if(derivationPath == null)
				throw new ArgumentNullException(nameof(derivationPath));
			if(extPubKey == null)
				throw new ArgumentNullException(nameof(extPubKey));
			_Network = network ?? throw new ArgumentNullException(nameof(network));
			_Repository = repository ?? throw new ArgumentNullException(nameof(repository));
			_ExtPubKey = extPubKey.ExtPubKey.Derive(derivationPath);
			_DerivationPath = derivationPath;
			_WalletId = "Wallet_" + Encoders.Base58.EncodeData(Hashes.Hash160(Encoding.UTF8.GetBytes(_ExtPubKey.ToString() + "-" + derivationPath.ToString())).ToBytes());
		}

		private readonly IRepository _Repository;
		private readonly string _WalletId;

		public IRepository Repository => _Repository;

		public Script GetNewDestination()
		{
			while(true)
			{
				var index = Repository.Get<uint>(_WalletId, "");
				var address = _ExtPubKey.Derive((uint)index).PubKey.Hash.ScriptPubKey;
				index++;
				var conflict = false;
				Repository.UpdateOrInsert(_WalletId, "", index, (o, n) =>
				{
					conflict = o + 1 != n;
					return n;
				});
				if(conflict)
					continue;
				Repository.UpdateOrInsert<uint?>(_WalletId, address.Hash.ToString(), (uint)(index - 1), (o, n) => n);

				var path = _DerivationPath.Derive((uint)(index - 1));
				Debug.WriteLine($"Created address {address.GetDestinationAddress(_Network)} of with HD path {path}");
				return address;
			}
		}

		public KeyPath GetKeyPath(Script script)
		{
			var index = Repository.Get<uint?>(_WalletId, script.Hash.ToString());
			if(index == null)
				return null;
			return _DerivationPath.Derive(index.Value);
		}
	}
}
