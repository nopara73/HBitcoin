using NTumbleBit.PuzzleSolver;
using System;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NTumbleBit.PuzzlePromise;
using NTumbleBit.Services;
using NTumbleBit.ClassicTumbler.Models;
using System.Threading;
using System.Diagnostics;

namespace NTumbleBit.ClassicTumbler.Client
{
	public class PaymentStateMachine
	{
		public TumblerClientRuntime Runtime
		{
			get; set;
		}
		public PaymentStateMachine(
			TumblerClientRuntime runtime)
		{
			Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		}




		public PaymentStateMachine(
			TumblerClientRuntime runtime,
			State state) : this(runtime)
		{
			if(state == null)
				return;
			if(state.NegotiationClientState != null)
			{
				StartCycle = state.NegotiationClientState.CycleStart;
				ClientChannelNegotiation = new ClientChannelNegotiation(runtime.TumblerParameters, state.NegotiationClientState);
			}
			if(state.PromiseClientState != null)
				PromiseClientSession = new PromiseClientSession(runtime.TumblerParameters.CreatePromiseParamaters(), state.PromiseClientState);
			if(state.SolverClientState != null)
				SolverClientSession = new SolverClientSession(runtime.TumblerParameters.CreateSolverParamaters(), state.SolverClientState);
			InvalidPhaseCount = state.InvalidPhaseCount;
		}

		public int InvalidPhaseCount
		{
			get; set;
		}

		public Tracker Tracker => Runtime.Tracker;

		public ExternalServices Services => Runtime.Services;

		public ClassicTumblerParameters Parameters => Runtime.TumblerParameters;

		public int StartCycle
		{
			get; set;
		}
		public ClientChannelNegotiation ClientChannelNegotiation
		{
			get; set;
		}

		public SolverClientSession SolverClientSession
		{
			get; set;
		}
		public PromiseClientSession PromiseClientSession
		{
			get;
			private set;
		}
		public IDestinationWallet DestinationWallet => Runtime.DestinationWallet;

		public bool Cooperative => Runtime.Cooperative;

		public class State
		{
			public ClientChannelNegotiation.State NegotiationClientState
			{
				get;
				set;
			}
			public PromiseClientSession.State PromiseClientState
			{
				get;
				set;
			}
			public SolverClientSession.State SolverClientState
			{
				get;
				set;
			}
			public int InvalidPhaseCount
			{
				get; set;
			}
		}

		public State GetInternalState()
		{
			var s = new State();
			if (SolverClientSession != null)
				s.SolverClientState = SolverClientSession.GetInternalState();
			if(PromiseClientSession != null)
				s.PromiseClientState = PromiseClientSession.GetInternalState();
			if(ClientChannelNegotiation != null)
				s.NegotiationClientState = ClientChannelNegotiation.GetInternalState();
			s.InvalidPhaseCount = InvalidPhaseCount;
			return s;
		}

		public async Task UpdateAsync(CancellationToken ctsToken)
		{
			var height = Services.BlockExplorerService.GetCurrentHeight();
			CycleParameters cycle;
			CyclePhase phase;
			if(ClientChannelNegotiation == null)
			{
				cycle = Parameters.CycleGenerator.GetRegistratingCycle(height);
				phase = CyclePhase.Registration;
			}
			else
			{
				cycle = ClientChannelNegotiation.GetCycle();
				var phases = new CyclePhase[]
				{
					CyclePhase.Registration,
					CyclePhase.ClientChannelEstablishment,
					CyclePhase.TumblerChannelEstablishment,
					CyclePhase.PaymentPhase,
					CyclePhase.TumblerCashoutPhase,
					CyclePhase.ClientCashoutPhase
				};
				if(!phases.Any(p => cycle.IsInPhase(p, height)))
					return;
				phase = phases.First(p => cycle.IsInPhase(p, height));
			}


			Debug.WriteLine("[[[Updating cycle " + cycle.Start + "]]]");

			Debug.WriteLine("Phase " + Enum.GetName(typeof(CyclePhase), phase) + ", ending in " + (cycle.GetPeriods().GetPeriod(phase).End - height) + " blocks");

			TumblerClient bob = null, alice = null;
			try
			{

				var correlation = SolverClientSession == null ? 0 : GetCorrelation(SolverClientSession.EscrowedCoin);

				FeeRate feeRate = null;
				switch(phase)
				{
					case CyclePhase.Registration:
						if(ClientChannelNegotiation == null)
						{
							bob = Runtime.CreateTumblerClient(new Identity(Role.Bob, cycle.Start));
							//Client asks for voucher
							var voucherResponse = await bob.AskUnsignedVoucherAsync(ctsToken).ConfigureAwait(false);
							//Client ensures he is in the same cycle as the tumbler (would fail if one tumbler or client's chain isn't sync)
							var tumblerCycle = Parameters.CycleGenerator.GetCycle(voucherResponse.CycleStart);
							Assert(tumblerCycle.Start == cycle.Start, "invalid-phase");
							//Saving the voucher for later
							StartCycle = cycle.Start;
							ClientChannelNegotiation = new ClientChannelNegotiation(Parameters, cycle.Start);
							ClientChannelNegotiation.ReceiveUnsignedVoucher(voucherResponse);
							Debug.WriteLine("Registered");
						}
						break;
					case CyclePhase.ClientChannelEstablishment:
						if(ClientChannelNegotiation.Status == TumblerClientSessionStates.WaitingTumblerClientTransactionKey)
						{
							alice = Runtime.CreateTumblerClient(new Identity(Role.Alice, cycle.Start));
							var key = await alice.RequestTumblerEscrowKeyAsync(ctsToken).ConfigureAwait(false);
							ClientChannelNegotiation.ReceiveTumblerEscrowKey(key.PubKey, key.KeyIndex);
							//Client create the escrow
							var escrowTxOut = ClientChannelNegotiation.BuildClientEscrowTxOut();
							feeRate = GetFeeRate();

							Transaction clientEscrowTx = null;
							try
							{
								clientEscrowTx = Services.WalletService.FundTransaction(escrowTxOut, feeRate);
							}
							catch(NotEnoughFundsException ex)
							{
								Debug.WriteLine($"Not enough funds in the wallet to tumble. Missing about {ex.Missing}. Denomination is {Parameters.Denomination}.");
								break;
							}

							var redeemDestination = Services.WalletService.GenerateAddress().ScriptPubKey;
							SolverClientSession = ClientChannelNegotiation.SetClientSignedTransaction(clientEscrowTx, redeemDestination);


							correlation = GetCorrelation(SolverClientSession.EscrowedCoin);

							Tracker.AddressCreated(cycle.Start, TransactionType.ClientEscrow, escrowTxOut.ScriptPubKey, correlation);
							Tracker.TransactionCreated(cycle.Start, TransactionType.ClientEscrow, clientEscrowTx.GetHash(), correlation);
							Services.BlockExplorerService.Track(escrowTxOut.ScriptPubKey);


							var redeemTx = SolverClientSession.CreateRedeemTransaction(feeRate);
							Tracker.AddressCreated(cycle.Start, TransactionType.ClientRedeem, redeemDestination, correlation);

							//redeemTx does not be to be recorded to the tracker, this is TrustedBroadcastService job

							Services.BroadcastService.Broadcast(clientEscrowTx);

							Services.TrustedBroadcastService.Broadcast(cycle.Start, TransactionType.ClientRedeem, correlation, redeemTx);

							Debug.WriteLine("Client channel broadcasted");
						}
						else if(ClientChannelNegotiation.Status == TumblerClientSessionStates.WaitingSolvedVoucher)
						{
							alice = Runtime.CreateTumblerClient(new Identity(Role.Alice, cycle.Start));
							var clientTx = GetTransactionInformation(SolverClientSession.EscrowedCoin, true);
							var state = ClientChannelNegotiation.GetInternalState();
							if(clientTx != null && clientTx.Confirmations >= cycle.SafetyPeriodDuration)
							{
								Debug.WriteLine($"Client escrow reached {cycle.SafetyPeriodDuration} confirmations");
								//Client asks the public key of the Tumbler and sends its own
								var voucher = await alice.SignVoucherAsync(new SignVoucherRequest
								{
									MerkleProof = clientTx.MerkleProof,
									Transaction = clientTx.Transaction,
									KeyReference = state.TumblerEscrowKeyReference,
									UnsignedVoucher = state.BlindedVoucher,
									Cycle = cycle.Start,
									ClientEscrowKey = state.ClientEscrowKey.PubKey
								}, ctsToken).ConfigureAwait(false);
								ClientChannelNegotiation.CheckVoucherSolution(voucher);
								Debug.WriteLine($"Tumbler escrow voucher obtained");
							}
						}
						break;
					case CyclePhase.TumblerChannelEstablishment:
						if(ClientChannelNegotiation != null && ClientChannelNegotiation.Status == TumblerClientSessionStates.WaitingGenerateTumblerTransactionKey)
						{
							bob = Runtime.CreateTumblerClient(new Identity(Role.Bob, cycle.Start));
							//Client asks the Tumbler to make a channel
							var bobEscrowInformation = ClientChannelNegotiation.GetOpenChannelRequest();
							var tumblerInformation = await bob.OpenChannelAsync(bobEscrowInformation, ctsToken).ConfigureAwait(false);
							PromiseClientSession = ClientChannelNegotiation.ReceiveTumblerEscrowedCoin(tumblerInformation);
							Debug.WriteLine("Tumbler escrow broadcasted");
							//Tell to the block explorer we need to track that address (for checking if it is confirmed in payment phase)
							Services.BlockExplorerService.Track(PromiseClientSession.EscrowedCoin.ScriptPubKey);
							Tracker.AddressCreated(cycle.Start, TransactionType.TumblerEscrow, PromiseClientSession.EscrowedCoin.ScriptPubKey, correlation);
							Tracker.TransactionCreated(cycle.Start, TransactionType.TumblerEscrow, PromiseClientSession.EscrowedCoin.Outpoint.Hash, correlation);

							//Channel is done, now need to run the promise protocol to get valid puzzle
							var cashoutDestination = DestinationWallet.GetNewDestination();
							Tracker.AddressCreated(cycle.Start, TransactionType.TumblerCashout, cashoutDestination, correlation);

							feeRate = GetFeeRate();
							var sigReq = PromiseClientSession.CreateSignatureRequest(cashoutDestination, feeRate);
							var commiments = await bob.SignHashesAsync(PromiseClientSession.Id, sigReq, ctsToken).ConfigureAwait(false);
							PromiseClientRevelation revelation = PromiseClientSession.Reveal(commiments);
							var proof = await bob.CheckRevelationAsync(PromiseClientSession.Id, revelation, ctsToken).ConfigureAwait(false);
							var puzzle = PromiseClientSession.CheckCommitmentProof(proof);
							SolverClientSession.AcceptPuzzle(puzzle);
							Debug.WriteLine("Tumbler escrow puzzle obtained");
						}
						break;
					case CyclePhase.PaymentPhase:
						if(PromiseClientSession != null)
						{
							var tumblerTx = GetTransactionInformation(PromiseClientSession.EscrowedCoin, false);
							//Ensure the tumbler coin is confirmed before paying anything
							if (tumblerTx != null || tumblerTx.Confirmations >= cycle.SafetyPeriodDuration)
							{
								Debug.WriteLine($"Client escrow reached {cycle.SafetyPeriodDuration} confirmations");

								if(SolverClientSession.Status == SolverClientStates.WaitingGeneratePuzzles)
								{
									feeRate = GetFeeRate();
									alice = Runtime.CreateTumblerClient(new Identity(Role.Alice, cycle.Start));
									var puzzles = SolverClientSession.GeneratePuzzles();
									var commmitments = await alice.SolvePuzzlesAsync(SolverClientSession.Id, puzzles, ctsToken).ConfigureAwait(false);
									SolverClientRevelation revelation2 = SolverClientSession.Reveal(commmitments);
									var solutionKeys = await alice.CheckRevelationAsync(SolverClientSession.Id, revelation2, ctsToken).ConfigureAwait(false);
									var blindFactors = SolverClientSession.GetBlindFactors(solutionKeys);
									var offerInformation = await alice.CheckBlindFactorsAsync(SolverClientSession.Id, blindFactors, ctsToken).ConfigureAwait(false);

									var offerSignature = SolverClientSession.SignOffer(offerInformation);

									var offerRedeem = SolverClientSession.CreateOfferRedeemTransaction(feeRate);
									//May need to find solution in the fulfillment transaction
									Services.BlockExplorerService.Track(offerRedeem.PreviousScriptPubKey);
									Tracker.AddressCreated(cycle.Start, TransactionType.ClientOfferRedeem, SolverClientSession.GetInternalState().RedeemDestination, correlation);
									Services.TrustedBroadcastService.Broadcast(cycle.Start, TransactionType.ClientOfferRedeem, correlation, offerRedeem);
									try
									{
										solutionKeys = await alice.FulfillOfferAsync(SolverClientSession.Id, offerSignature, ctsToken).ConfigureAwait(false);
										SolverClientSession.CheckSolutions(solutionKeys);
										var tumblingSolution = SolverClientSession.GetSolution();
										var transaction = PromiseClientSession.GetSignedTransaction(tumblingSolution);
										Debug.WriteLine("Got puzzle solution cooperatively from the tumbler");
										Services.TrustedBroadcastService.Broadcast(cycle.Start, TransactionType.TumblerCashout, correlation, new TrustedBroadcastRequest
										{
											BroadcastAt = cycle.GetPeriods().ClientCashout.Start,
											Transaction = transaction
										});
										if(Cooperative)
										{
											var signature = SolverClientSession.SignEscape();
											await alice.GiveEscapeKeyAsync(SolverClientSession.Id, signature, ctsToken).ConfigureAwait(false);
											Debug.WriteLine("Gave escape signature to the tumbler");
										}
									}
									catch(Exception ex)
									{
										Debug.WriteLine("WARNING: The tumbler did not gave puzzle solution cooperatively");
										Debug.WriteLine("WARNING: " + ex.ToString());
									}
								}
							}
						}
						break;
					case CyclePhase.ClientCashoutPhase:
						if(SolverClientSession != null)
						{
							//If the tumbler is uncooperative, he published solutions on the blockchain
							if(SolverClientSession.Status == SolverClientStates.WaitingPuzzleSolutions)
							{
								var transactions = Services.BlockExplorerService.GetTransactions(SolverClientSession.GetInternalState().OfferCoin.ScriptPubKey, false);
								if(transactions.Length != 0)
								{
									SolverClientSession.CheckSolutions(transactions.Select(t => t.Transaction).ToArray());
									Debug.WriteLine("Puzzle solution recovered from tumbler's fulfill transaction");

									var tumblingSolution = SolverClientSession.GetSolution();
									var transaction = PromiseClientSession.GetSignedTransaction(tumblingSolution);
									Tracker.TransactionCreated(cycle.Start, TransactionType.TumblerCashout, transaction.GetHash(), correlation);
									Services.BroadcastService.Broadcast(transaction);
								}
							}
						}
						break;
				}
			}
			finally
			{
				if(alice != null && bob != null)
					throw new InvalidOperationException("Bob and Alice have been both initialized, please report the bug to NTumbleBit developers");
			}
		}

		private static uint GetCorrelation(ScriptCoin escrowCoin) => new uint160(escrowCoin.Redeem.Hash.ToString()).GetLow32();

		private TransactionInformation GetTransactionInformation(ICoin coin, bool withProof)
		{
			var tx = Services.BlockExplorerService
				.GetTransactions(coin.TxOut.ScriptPubKey, withProof)
				.FirstOrDefault(t => t.Transaction.Outputs.AsCoins().Any(c => c.Outpoint == coin.Outpoint));
			return tx;
		}

		private FeeRate GetFeeRate() => Services.FeeService.GetFeeRate();

		private static void Assert(bool test, string error)
		{
			if(!test)
				throw new PuzzleException(error);
		}
	}
}
