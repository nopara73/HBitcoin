using NBitcoin;
using HBitcoin.TumbleBit.ClassicTumbler.Models;
using HBitcoin.TumbleBit.PuzzlePromise;
using HBitcoin.TumbleBit.PuzzleSolver;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace HBitcoin.TumbleBit.ClassicTumbler.Client
{
	public class TumblerClient
	{
		public TumblerClient(Network network, Uri serverAddress, Identity identity, TumblerClientRuntime runtime)
		{
			_Address = serverAddress ?? throw new ArgumentNullException(nameof(serverAddress));
			_Network = network ?? throw new ArgumentNullException(nameof(network));
			_identity = identity;
			_runtime = runtime;
			ClassicTumblerParameters.ExtractHashFromUrl(serverAddress); //Validate
		}

		private TumblerClientRuntime _runtime;
		private Identity _identity;

		private readonly Network _Network;
		public Network Network => _Network;

		private readonly Uri _Address;
		
		public async Task<ClassicTumblerParameters> GetTumblerParametersAsync(CancellationToken ctsToken) 
			=> await GetAsync<ClassicTumblerParameters>(ctsToken, $"parameters").ConfigureAwait(false);

		private async Task<T> GetAsync<T>(CancellationToken ctsToken, string relativePath, params object[] parameters) 
			=> await SendAsync<T>(ctsToken, HttpMethod.Get, null, relativePath, parameters).ConfigureAwait(false);
		
		public async Task<UnsignedVoucherInformation> AskUnsignedVoucherAsync(CancellationToken ctsToken)
			=> await GetAsync<UnsignedVoucherInformation>(ctsToken, $"vouchers/").ConfigureAwait(false);

		public async Task<PuzzleSolution> SignVoucherAsync(SignVoucherRequest signVoucherRequest, CancellationToken ctsToken)
			=> await SendAsync<PuzzleSolution>(ctsToken, HttpMethod.Post, signVoucherRequest, $"clientchannels/confirm").ConfigureAwait(false);

		public async Task<ScriptCoin> OpenChannelAsync(OpenChannelRequest request, CancellationToken ctsToken)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			return await SendAsync<ScriptCoin>(ctsToken, HttpMethod.Post, request, $"channels/").ConfigureAwait(false);
		}

		public async Task<TumblerEscrowKeyResponse> RequestTumblerEscrowKeyAsync(CancellationToken ctsToken)
			=> await SendAsync<TumblerEscrowKeyResponse>(ctsToken, HttpMethod.Post, _identity.CycleId, $"clientchannels/").ConfigureAwait(false);

		private string GetFullUri(string relativePath, params object[] parameters)
		{
			relativePath = String.Format(relativePath, parameters ?? new object[0]);
			var uri = _Address.AbsoluteUri;
			if (!uri.EndsWith("/", StringComparison.Ordinal))
				uri += "/";
			uri += relativePath;
			return uri;
		}

		public static Identity LastUsedIdentity { get; private set; } = Identity.DoesntMatter;

		private async Task<T> SendAsync<T>(CancellationToken ctsToken, HttpMethod method, object body, string relativePath, params object[] parameters)
		{
			var uri = GetFullUri(relativePath, parameters);
			var message = new HttpRequestMessage(method, uri);
			if (body != null)
			{
				message.Content = new StringContent(Serializer.ToString(body, Network), Encoding.UTF8, "application/json");
			}

			// torchangelog.txt for testing only, before merge to master it should be deleted
			if (_identity == new Identity(Role.Alice, -1))
			{
				File.AppendAllText("torchangelog.txt", Environment.NewLine + Environment.NewLine + "//RESTART" + Environment.NewLine);
			}

			if (_identity != LastUsedIdentity)
			{
				var start = DateTime.Now;
				Debug.WriteLine($"Changing identity to {_identity}");
				await _runtime.WalletJob.ControlPortClient.ChangeCircuitAsync(ctsToken).ConfigureAwait(false);
				var takelong = DateTime.Now - start;
				File.AppendAllText("torchangelog.txt", Environment.NewLine + Environment.NewLine + $"CHANGE IP: {(int)takelong.TotalSeconds} sec" + Environment.NewLine);
			}
			LastUsedIdentity = _identity;
			File.AppendAllText("torchangelog.txt", '\t' + _identity.ToString() + Environment.NewLine);
			File.AppendAllText("torchangelog.txt", '\t' + message.Method.Method + " " + message.RequestUri.AbsolutePath + Environment.NewLine);

			HttpResponseMessage result;
			try
			{
				result = await _runtime.WalletJob.TorHttpClient.SendAsync(message, ctsToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				File.AppendAllText("torchangelog.txt", ex.ToString() + Environment.NewLine);
				throw;
			}
			if(result.StatusCode != HttpStatusCode.OK)
			{
				File.AppendAllText("torchangelog.txt", message.ToHttpStringAsync() + Environment.NewLine);
				File.AppendAllText("torchangelog.txt", result.ToHttpStringAsync() + Environment.NewLine);
			}

			if (result.StatusCode == HttpStatusCode.NotFound)
				return default(T);
			if (!result.IsSuccessStatusCode)
			{
				var error = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
				if (!string.IsNullOrEmpty(error))
				{
					throw new HttpRequestException(result.StatusCode + ": " + error);
				}
			}
			result.EnsureSuccessStatusCode();
			if (typeof(T) == typeof(byte[]))
				return (T)(object)await result.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
			var str = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
			if (typeof(T) == typeof(string))
				return (T)(object)str;
			return Serializer.ToObject<T>(str, Network);
		}

		public async Task<ServerCommitmentsProof> CheckRevelationAsync(string channelId, PromiseClientRevelation revelation, CancellationToken ctsToken)
			=> await SendAsync<ServerCommitmentsProof>(ctsToken, HttpMethod.Post, revelation, $"channels/{_identity.CycleId}/{channelId}/checkrevelation").ConfigureAwait(false);

		public async Task<PuzzlePromise.ServerCommitment[]> SignHashesAsync(string channelId, SignaturesRequest sigReq, CancellationToken ctsToken)
			=> await SendAsync<PuzzlePromise.ServerCommitment[]>(ctsToken, HttpMethod.Post, sigReq, $"channels/{_identity.CycleId}/{channelId}/signhashes").ConfigureAwait(false);

		public async Task<SolutionKey[]> CheckRevelationAsync(string channelId, SolverClientRevelation revelation, CancellationToken ctsToken)
			=> await SendAsync<SolutionKey[]>(ctsToken, HttpMethod.Post, revelation, $"clientschannels/{_identity.CycleId}/{channelId}/checkrevelation").ConfigureAwait(false);
		
		public async Task<OfferInformation> CheckBlindFactorsAsync(string channelId, BlindFactor[] blindFactors, CancellationToken ctsToken)
			=> await SendAsync<OfferInformation>(ctsToken, HttpMethod.Post, blindFactors, $"clientschannels/{_identity.CycleId}/{channelId}/checkblindfactors").ConfigureAwait(false);
		
		public async Task<PuzzleSolver.ServerCommitment[]> SolvePuzzlesAsync(string channelId, PuzzleValue[] puzzles, CancellationToken ctsToken)
			=> await SendAsync<PuzzleSolver.ServerCommitment[]>(ctsToken, HttpMethod.Post, puzzles, $"clientchannels/{_identity.CycleId}/{channelId}/solvepuzzles").ConfigureAwait(false);
		
		public async Task<SolutionKey[]> FulfillOfferAsync(string channelId, TransactionSignature signature, CancellationToken ctsToken)
			=> await SendAsync<SolutionKey[]>(ctsToken, HttpMethod.Post, signature, $"clientchannels/{_identity.CycleId}/{channelId}/offer").ConfigureAwait(false);
		
		public async Task GiveEscapeKeyAsync(string channelId, TransactionSignature signature, CancellationToken ctsToken)
			=> await SendAsync<string>(ctsToken, HttpMethod.Post, signature, $"clientchannels/{_identity.CycleId}/{channelId}/escape").ConfigureAwait(false);
	}
}
