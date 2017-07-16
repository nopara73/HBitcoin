using System;
using System.Collections.Generic;
using NBitcoin;
using System.IO;
using NBitcoin.RPC;
using System.Threading.Tasks;
using NTumbleBit.Services;
using NTumbleBit.Configuration;
using System.Threading;
using System.Diagnostics;

namespace NTumbleBit.ClassicTumbler.Client
{
	public class PrematureRequestException : Exception
	{
		public PrematureRequestException() : base("Premature request")
		{

		}
	}

	public class TumblerClientRuntime : IDisposable
	{
		public static async Task<TumblerClientRuntime> FromConfigurationAsync(TumblerClientConfiguration configuration, CancellationToken ctsToken)
		{
			var runtime = new TumblerClientRuntime();
			try
			{
				await runtime.ConfigureAsync(configuration, ctsToken).ConfigureAwait(false);
			}
			catch
			{
				runtime.Dispose();
				throw;
			}
			return runtime;
		}
		public async Task ConfigureAsync(TumblerClientConfiguration configuration, CancellationToken ctsToken)
		{
			Network = configuration.Network;
			TumblerServer = configuration.TumblerServer;

			RPCClient rpc = null;
			try
			{
				rpc = configuration.RPCArgs.ConfigureRPCClient(configuration.Network);
			}
			catch
			{
				throw new ConfigException("Please, fix rpc settings in " + configuration.ConfigurationFile);
			}

			var dbreeze = new DBreezeRepository(Path.Combine(configuration.DataDir, "db2"));
			Cooperative = configuration.Cooperative;
			Repository = dbreeze;
			_Disposables.Add(dbreeze);
			Tracker = new Tracker(dbreeze, Network);
			Services = ExternalServices.CreateFromRPCClient(rpc, dbreeze, Tracker);

			if(configuration.OutputWallet.RootKey != null && configuration.OutputWallet.KeyPath != null)
				DestinationWallet = new ClientDestinationWallet(configuration.OutputWallet.RootKey, configuration.OutputWallet.KeyPath, dbreeze, configuration.Network);
			else if(configuration.OutputWallet.RPCArgs != null)
			{
				try
				{
					DestinationWallet = new RPCDestinationWallet(configuration.OutputWallet.RPCArgs.ConfigureRPCClient(Network));
				}
				catch
				{
					throw new ConfigException("Please, fix outputwallet rpc settings in " + configuration.ConfigurationFile);
				}
			}
			else
				throw new ConfigException("Missing configuration for outputwallet");

			TumblerParameters = dbreeze.Get<ClassicTumblerParameters>("Configuration", configuration.TumblerServer.AbsoluteUri);
			var parameterHash = ClassicTumblerParameters.ExtractHashFromUrl(configuration.TumblerServer);

			if(TumblerParameters != null && TumblerParameters.GetHash() != parameterHash)
				TumblerParameters = null;

			if(!configuration.OnlyMonitor)
			{
				var client = CreateTumblerClient(new Identity(Role.Alice, -1));
				if(TumblerParameters == null)
				{
					Debug.WriteLine("Downloading tumbler information of " + configuration.TumblerServer.AbsoluteUri);
					var parameters = await Retry(3, async () 
						=> await client.GetTumblerParametersAsync(ctsToken).ConfigureAwait(false)).ConfigureAwait(false);
					if(parameters == null)
						throw new ConfigException("Unable to download tumbler's parameters");

					if(parameters.GetHash() != parameterHash)
						throw new ConfigException("The tumbler returned an invalid configuration");

					var standardCycles = new StandardCycles(configuration.Network);
					var standardCycle = standardCycles.GetStandardCycle(parameters);

					if(standardCycle == null || !parameters.IsStandard())
					{
						Debug.WriteLine("WARNING: This tumbler has non standard parameters");
						standardCycle = null;
					}

					Repository.UpdateOrInsert("Configuration", TumblerServer.AbsoluteUri, parameters, (o, n) => n);
					TumblerParameters = parameters;

					Debug.WriteLine("Tumbler parameters saved");
				}

				Debug.WriteLine($"Using tumbler {TumblerServer.AbsoluteUri}");
			}
		}

		public BroadcasterJob CreateBroadcasterJob() => new BroadcasterJob(Services);

		public bool Cooperative { get; set; }

		public Uri TumblerServer { get; set; }

		public TumblerClient CreateTumblerClient(Identity identity)
		{
			var client = new TumblerClient(Network, TumblerServer, identity);
			return client;
		}

		public StateMachinesExecutor CreateStateMachineJob() => new StateMachinesExecutor(this);

		private static T Retry<T>(int count, Func<T> act)
		{
			var exceptions = new List<Exception>();
			for(int i = 0; i < count; i++)
			{
				try
				{
					return act();
				}
				catch(Exception ex)
				{
					exceptions.Add(ex);
				}
			}
			throw new AggregateException(exceptions);
		}

		List<IDisposable> _Disposables = new List<IDisposable>();

		public void Dispose()
		{
			foreach(var disposable in _Disposables)
				disposable.Dispose();
			_Disposables.Clear();
		}

		public IDestinationWallet DestinationWallet { get; set; }
		public Network Network { get; set; }
		public ExternalServices Services { get; set; }
		public Tracker Tracker { get; set; }
		public ClassicTumblerParameters TumblerParameters { get; set; }
		public DBreezeRepository Repository { get; set; }
	}
}
