using NBitcoin;
using NBitcoin.RPC;
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace HBitcoin.TumbleBit.Configuration
{
	public class RPCArgs
	{
		public Uri Url
		{
			get; set;
		}
		public string User
		{
			get; set;
		}
		public string Password
		{
			get; set;
		}
		public string CookieFile
		{
			get; set;
		}
		public RPCClient ConfigureRPCClient(Network network)
		{
			RPCClient rpcClient = null;
			var url = Url;
			var usr = User;
			var pass = Password;
			if(url != null && usr != null && pass != null)
				rpcClient = new RPCClient(new System.Net.NetworkCredential(usr, pass), url, network);
			if(rpcClient == null)
			{
				if(url != null && CookieFile != null)
				{
					try
					{

						rpcClient = new RPCClient(File.ReadAllText(CookieFile), url, network);
					}
					catch(IOException)
					{
						Debug.WriteLine("WARNING: RPC Cookie file not found at " + CookieFile);
					}
				}

				if(rpcClient == null)
				{
					try
					{
						rpcClient = new RPCClient(network);
					}
					catch { }
					if(rpcClient == null)
					{
						Debug.WriteLine("ERROR: RPC connection settings not configured");
						throw new ConfigException();
					}
				}
			}

			Debug.WriteLine("Testing RPC connection to " + rpcClient.Address.AbsoluteUri);
			try
			{
				var address = new Key().PubKey.GetAddress(network);
				var isValid = ((JObject)rpcClient.SendCommand("validateaddress", address.ToString()).Result)["isvalid"].Value<bool>();
				if(!isValid)
				{
					Debug.WriteLine("ERROR: The RPC Server is on a different blockchain than the one configured for tumbling");
					throw new ConfigException();
				}
			}
			catch(ConfigException)
			{
				throw;
			}
			catch(RPCException ex)
			{
				Debug.WriteLine("ERROR: Invalid response from RPC server " + ex.Message);
				throw new ConfigException();
			}
			catch(Exception ex)
			{
				Debug.WriteLine("ERROR: Error connecting to RPC server " + ex.Message);
				throw new ConfigException();
			}
			Debug.WriteLine("RPC connection successfull");

			if(rpcClient.GetBlockHash(0) != network.GenesisHash)
			{
				Debug.WriteLine("ERROR: The RPC server is not using the chain " + network.Name);
				throw new ConfigException();
			}
			var getInfo = rpcClient.SendCommand(RPCOperations.getinfo);
			var version = ((JObject)getInfo.Result)["version"].Value<int>();
			if(version < MIN_CORE_VERSION)
			{
				Debug.WriteLine($"ERROR: The minimum Bitcoin Core version required is {MIN_CORE_VERSION} (detected: {version})");
				throw new ConfigException();
			}
			Debug.WriteLine($"Bitcoin Core version detected: {version}");
			return rpcClient;
		}

		const int MIN_CORE_VERSION = 130100;
		public static RPCClient ConfigureRPCClient(TextFileConfiguration confArgs, Network network, string prefix = null)
		{
			var args = Parse(confArgs, network, prefix);
			return args.ConfigureRPCClient(network);
		}

		public static RPCArgs Parse(TextFileConfiguration confArgs, Network network, string prefix = null)
		{
			prefix = prefix ?? "";
			if(prefix != "")
			{
				if(!prefix.EndsWith("."))
					prefix += ".";
			}
			try
			{
				var url = confArgs.GetOrDefault<string>(prefix + "rpc.url", network == null ? null : "http://localhost:" + network.RPCPort + "/");
				return new RPCArgs
				{
					User = confArgs.GetOrDefault<string>(prefix + "rpc.user", null),
					Password = confArgs.GetOrDefault<string>(prefix + "rpc.password", null),
					CookieFile = confArgs.GetOrDefault<string>(prefix + "rpc.cookiefile", null),
					Url = url == null ? null : new Uri(url)
				};
			}
			catch(FormatException)
			{
				throw new ConfigException("rpc.url is not an url");
			}
		}
	}
}
