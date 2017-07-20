using System;
using System.Collections.Generic;
using NBitcoin;
using System.IO;
using System.Threading.Tasks;
using HBitcoin.TumbleBit.Services;
using System.Threading;
using System.Diagnostics;
using DotNetTor;
using System.Net.Http;
using HBitcoin.FullBlockSpv;
using HBitcoin.KeyManagement;

namespace HBitcoin.TumbleBit.ClassicTumbler.Client
{
	public class PrematureRequestException : Exception
	{
		public PrematureRequestException() : base("Premature request")
		{

		}
	}

	public class TumblerClientRuntime : IDisposable
	{
		public WalletJob WalletJob { get; private set; }
		public static async Task<TumblerClientRuntime> FromConfigurationAsync(TumblerClientConfiguration configuration, WalletJob walletJob, CancellationToken ctsToken)
		{
			var runtime = new TumblerClientRuntime();
			try
			{
				await runtime.ConfigureAsync(configuration, walletJob, ctsToken).ConfigureAwait(false);
			}
			catch
			{
				runtime.Dispose();
				throw;
			}
			return runtime;
		}
		public async Task ConfigureAsync(TumblerClientConfiguration configuration, WalletJob walletJob, CancellationToken ctsToken)
		{
			WalletJob = walletJob;

			Network = configuration.Network;
			TumblerServer = configuration.TumblerServer;

			var dbreeze = new DBreezeRepository(Path.Combine(configuration.DataDir, "db2"));
			Cooperative = configuration.Cooperative;
			Repository = dbreeze;
			_Disposables.Add(dbreeze);
			Tracker = new Services.Tracker(dbreeze, Network);
			Services = ExternalServices.CreateFromHBitcoinClient(walletJob, dbreeze, Tracker);

			TumblerParameters = dbreeze.Get<ClassicTumblerParameters>("Configuration", configuration.TumblerServer.AbsoluteUri);
			var parameterHash = ClassicTumblerParameters.ExtractHashFromUrl(configuration.TumblerServer);

			if(TumblerParameters != null && TumblerParameters.GetHash() != parameterHash)
				TumblerParameters = null;

			var client = CreateTumblerClient(new Identity(Role.Alice, -1));

			Debug.WriteLine("Downloading tumbler information of " + configuration.TumblerServer.AbsoluteUri);
			var parameters = await client.GetTumblerParametersAsync(ctsToken).ConfigureAwait(false);
			if (parameters == null)
				throw new HttpRequestException("Unable to download tumbler's parameters");

			if (parameters.GetHash() != parameterHash)
				throw new ArgumentException("The tumbler returned an invalid configuration");

			var standardCycles = new StandardCycles(configuration.Network);
			var standardCycle = standardCycles.GetStandardCycle(parameters);

			if (standardCycle == null || !parameters.IsStandard())
			{
				Debug.WriteLine("WARNING: This tumbler has non standard parameters");
				standardCycle = null;
			}

			if(TumblerParameters == null)
			{
				TumblerParameters = parameters;
				Repository.UpdateOrInsert("Configuration", TumblerServer.AbsoluteUri, parameters, (o, n) => n);
				Debug.WriteLine("Tumbler parameters saved");
				Debug.WriteLine($"Using tumbler {TumblerServer.AbsoluteUri}");
			}
			else
			{
				if(TumblerParameters.GetHash() != parameters.GetHash())
				{
					throw new NotSupportedException("The tumbler changed its parameters.");
				}
			}
		}

		public BroadcasterJob CreateBroadcasterJob() => new BroadcasterJob(Services);

		public bool Cooperative { get; set; }

		public Uri TumblerServer { get; set; }

		public TumblerClient CreateTumblerClient(Identity identity)
		{
			var client = new TumblerClient(Network, TumblerServer, identity, this);
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
		
		public Network Network { get; set; }
		public ExternalServices Services { get; set; }
		public Services.Tracker Tracker { get; set; }
		public ClassicTumblerParameters TumblerParameters { get; set; }
		public DBreezeRepository Repository { get; set; }
	}
}
