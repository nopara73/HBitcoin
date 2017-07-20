using System;
using System.Text;
using System.Linq;
using System.IO;
using NBitcoin;
using System.Diagnostics;
using System.Net.Http;

namespace HBitcoin.TumbleBit.ClassicTumbler.Client
{
	public class TumblerClientConfiguration
	{
		public string DataDir { get; set; }
		public Network Network { get; set; }
		public bool Cooperative { get; set; }
		public Uri TumblerServer { get; set; }

		public TumblerClientConfiguration Load(Network netwok, Uri tumblerServer)
		{
			Network = netwok;
			DataDir = Path.Combine("TumbleBitData", Network.ToString());
			Directory.CreateDirectory(DataDir);
			Debug.WriteLine("Network: " + Network);
			Debug.WriteLine("Data directory set to " + DataDir);
			
			Cooperative = true;
			TumblerServer = tumblerServer;

			try
			{
				ClassicTumblerParameters.ExtractHashFromUrl(TumblerServer);
			}
			catch(FormatException)
			{
				throw new Exception("tumbler.server does not contains the parameter hash");
			}

			return this;
		}
	}
}
