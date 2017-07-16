using System;
using System.Text;
using System.Linq;
using System.IO;
using NBitcoin;
using HBitcoin.TumbleBit.Configuration;
using System.Diagnostics;
using System.Net.Http;

namespace HBitcoin.TumbleBit.ClassicTumbler.Client
{
	public class OutputWalletConfiguration
	{
		public BitcoinExtPubKey RootKey { get; set; }
		public KeyPath KeyPath { get; set; }
		public RPCArgs RPCArgs { get; set; }
	}

	public class TumblerClientConfiguration
	{
		public string DataDir { get; set; }
		public Network Network { get; set; }
		public bool Cooperative { get; set; }
		public Uri TumblerServer { get; set; }
		public OutputWalletConfiguration OutputWallet { get; set; } = new OutputWalletConfiguration();
		public RPCArgs RPCArgs { get; set; } = new RPCArgs();

		public TumblerClientConfiguration Load(Network netwok, Uri tumblerServer)
		{
			Network = netwok;
			DataDir = Path.Combine("TumbleBitData", Network.ToString());
			Directory.CreateDirectory(DataDir);
			Debug.WriteLine("Network: " + Network);
			Debug.WriteLine("Data directory set to " + DataDir);
			
			Cooperative = true;
			TumblerServer = tumblerServer;

			RPCArgs = RPCArgs.Parse(Network);

			try
			{
				ClassicTumblerParameters.ExtractHashFromUrl(TumblerServer);
			}
			catch(FormatException)
			{
				throw new Exception("tumbler.server does not contains the parameter hash");
			}

			var key = "tpubDCeHeZ4A66VU78YDJ1yKtnR7uVPf8rRU1thtCXtyzZ3XQXBqc3HFaqMPH1fxESjFvR4CyhyDqT3NuNKSnWc5HC6dD8cePbTaEUU6HF1MUND";
			OutputWallet.RootKey = new BitcoinExtPubKey(key, Network);

			OutputWallet.KeyPath = new KeyPath("");

			OutputWallet.RPCArgs = RPCArgs.Parse(Network, "outputwallet");

			return this;
		}
	}
}
