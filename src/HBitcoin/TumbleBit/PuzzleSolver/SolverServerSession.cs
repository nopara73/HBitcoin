using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NTumbleBit.PuzzleSolver
{
	public enum SolverServerStates
	{
		WaitingEscrow,
		WaitingPuzzles,
		WaitingRevelation,
		WaitingBlindFactor,
		WaitingFulfillment,
		WaitingEscape,
		Completed
	}
	public class SolverServerSession : EscrowReceiver
	{
		public class SolvedPuzzle
		{
			public SolvedPuzzle()
			{

			}
			public SolvedPuzzle(PuzzleValue puzzle, SolutionKey key, PuzzleSolution solution)
			{
				Puzzle = puzzle;
				SolutionKey = key;
				Solution = solution;
			}

			public PuzzleValue Puzzle
			{
				get; set;
			}
			public SolutionKey SolutionKey
			{
				get; set;
			}
			public PuzzleSolution Solution
			{
				get; set;
			}
		}

		public new class State : EscrowReceiver.State
		{
			public SolverServerStates Status
			{
				get; set;
			}

			public SolvedPuzzle[] SolvedPuzzles
			{
				get; set;
			}
			public Key FulfillKey
			{
				get;
				set;
			}
			public TransactionSignature OfferClientSignature
			{
				get;
				set;
			}
			public int ETag
			{
				get;
				set;
			}
			public ScriptCoin OfferCoin
			{
				get;
				set;
			}

			public PubKey GetClientEscrowPubKey() => EscrowScriptPubKeyParameters.GetFromCoin(EscrowedCoin).Initiator;
		}


		public State GetInternalState() => Serializer.Clone(InternalState);

		protected new State InternalState
		{
			get
			{
				return (State)base.InternalState;
			}
			set
			{
				base.InternalState = value;
			}
		}

		public SolverServerSession(RsaKey serverKey) : this(serverKey, null)
		{
		}

		public SolverServerSession(RsaKey serverKey, SolverParameters parameters)
		{
			parameters = parameters ?? new SolverParameters(serverKey.PubKey);
			if(serverKey == null)
				throw new ArgumentNullException(nameof(serverKey));
			if(serverKey.PubKey != parameters.ServerKey)
				throw new ArgumentNullException($"Private key not matching expected public key: {nameof(serverKey.PubKey)} != {nameof(parameters.ServerKey)}");
			InternalState = new State();
			_ServerKey = serverKey;
			_Parameters = parameters;
		}

		public SolverServerSession(RsaKey serverKey, SolverParameters parameters, State state)
			: this(serverKey, parameters)
		{
			if(state == null)
				return;
			InternalState = state;
		}


		private readonly RsaKey _ServerKey;
		public RsaKey ServerKey => _ServerKey;

		private SolverParameters _Parameters;
		public SolverParameters Parameters => _Parameters;

		public SolverServerStates Status => InternalState.Status;

		public override void ConfigureEscrowedCoin(ScriptCoin escrowedCoin, Key escrowKey)
		{
			AssertState(SolverServerStates.WaitingEscrow);
			base.ConfigureEscrowedCoin(escrowedCoin, escrowKey);
			InternalState.Status = SolverServerStates.WaitingPuzzles;
		}

		public ServerCommitment[] SolvePuzzles(PuzzleValue[] puzzles)
		{
			if(puzzles == null)
				throw new ArgumentNullException(nameof(puzzles));
			if(puzzles.Length != Parameters.GetTotalCount())
				throw new ArgumentException("Expecting " + Parameters.GetTotalCount() + " puzzles");
			AssertState(SolverServerStates.WaitingPuzzles);
			var commitments = new List<ServerCommitment>();
			var solvedPuzzles = new List<SolvedPuzzle>();
			foreach (var puzzle in puzzles)
			{
				var solution = puzzle.Solve(ServerKey);
				byte[] key = null;
				var encryptedSolution = Utils.ChachaEncrypt(solution.ToBytes(), ref key);
				var solutionKey = new SolutionKey(key);
				var keyHash = solutionKey.GetHash();
				commitments.Add(new ServerCommitment(keyHash, encryptedSolution));
				solvedPuzzles.Add(new SolvedPuzzle(puzzle, solutionKey, solution));
			}
			InternalState.SolvedPuzzles = solvedPuzzles.ToArray();
			InternalState.Status = SolverServerStates.WaitingRevelation;
			return commitments.ToArray();
		}

		public SolutionKey[] CheckRevelation(SolverClientRevelation revelation)
		{
			if(revelation == null)
				throw new ArgumentNullException($"{nameof(revelation)}");
			if(revelation.FakeIndexes.Length != Parameters.FakePuzzleCount || revelation.Solutions.Length != Parameters.FakePuzzleCount)
				throw new ArgumentException("Expecting " + Parameters.FakePuzzleCount + " puzzle solutions");
			AssertState(SolverServerStates.WaitingRevelation);



			var fakePuzzles = new List<SolvedPuzzle>();
			for (int i = 0; i < Parameters.FakePuzzleCount; i++)
			{
				var index = revelation.FakeIndexes[i];
				var solvedPuzzle = InternalState.SolvedPuzzles[index];
				if(solvedPuzzle.Solution != revelation.Solutions[i])
				{
					throw new PuzzleException("Incorrect puzzle solution");
				}
				fakePuzzles.Add(solvedPuzzle);
			}

			var realPuzzles = new List<SolvedPuzzle>();
			for (int i = 0; i < Parameters.GetTotalCount(); i++)
			{
				if(Array.IndexOf(revelation.FakeIndexes, i) == -1)
				{
					realPuzzles.Add(InternalState.SolvedPuzzles[i]);
				}
			}
			InternalState.SolvedPuzzles = realPuzzles.ToArray();
			InternalState.Status = SolverServerStates.WaitingBlindFactor;
			return fakePuzzles.Select(f => f.SolutionKey).ToArray();
		}

		public OfferInformation CheckBlindedFactors(BlindFactor[] blindFactors, FeeRate feeRate)
		{
			if(blindFactors == null)
				throw new ArgumentNullException(nameof(blindFactors));
			if(blindFactors.Length != Parameters.RealPuzzleCount)
				throw new ArgumentException("Expecting " + Parameters.RealPuzzleCount + " blind factors");
			AssertState(SolverServerStates.WaitingBlindFactor);
			Puzzle unblindedPuzzle = null;
			var y = 0;
			for (int i = 0; i < Parameters.RealPuzzleCount; i++)
			{
				var solvedPuzzle = InternalState.SolvedPuzzles[i];
				var unblinded = new Puzzle(Parameters.ServerKey, solvedPuzzle.Puzzle).Unblind(blindFactors[i]);
				if(unblindedPuzzle == null)
					unblindedPuzzle = unblinded;
				else if(unblinded != unblindedPuzzle)
					throw new PuzzleException("Invalid blind factor");
				y++;
			}

			InternalState.FulfillKey = new Key();

			var dummy = new Transaction();
			dummy.AddInput(new TxIn(InternalState.EscrowedCoin.Outpoint));
			dummy.Inputs[0].ScriptSig = new Script(
				Op.GetPushOp(TrustedBroadcastRequest.PlaceholderSignature),
				Op.GetPushOp(TrustedBroadcastRequest.PlaceholderSignature),
				Op.GetPushOp(InternalState.EscrowedCoin.Redeem.ToBytes())
			);
			dummy.AddOutput(new TxOut(InternalState.EscrowedCoin.Amount, new Key().ScriptPubKey.Hash));

			var offerTransactionFee = feeRate.GetFee(dummy.GetVirtualSize());


			var escrow = InternalState.EscrowedCoin;
			var escrowInformation = EscrowScriptPubKeyParameters.GetFromCoin(InternalState.EscrowedCoin);
			var redeem = new OfferScriptPubKeyParameters
			{
				Hashes = InternalState.SolvedPuzzles.Select(p => p.SolutionKey.GetHash()).ToArray(),
				FulfillKey = InternalState.FulfillKey.PubKey,
				Expiration = escrowInformation.LockTime,
				RedeemKey = escrowInformation.Initiator
			}.ToScript();
			var txOut = new TxOut(escrow.Amount - offerTransactionFee, redeem.Hash.ScriptPubKey);
			InternalState.OfferCoin = new Coin(escrow.Outpoint, txOut).ToScriptCoin(redeem);
			InternalState.Status = SolverServerStates.WaitingFulfillment;
			return new OfferInformation
			{
				FulfillKey = InternalState.FulfillKey.PubKey,
				Fee = offerTransactionFee
			};
		}


		Transaction GetUnsignedOfferTransaction()
		{
			var tx = new Transaction();
			tx.AddInput(new TxIn(InternalState.EscrowedCoin.Outpoint));
			tx.AddOutput(InternalState.OfferCoin.TxOut);
			return tx;
		}

		public TrustedBroadcastRequest GetSignedOfferTransaction()
		{
			AssertState(SolverServerStates.WaitingEscape);
			var offerTransaction = GetUnsignedOfferTransaction();
			offerTransaction.Inputs[0].PrevOut = new OutPoint();
			offerTransaction.Inputs[0].ScriptSig = new Script(
					Op.GetPushOp(InternalState.OfferClientSignature.ToBytes()),
					Op.GetPushOp(CreateOfferSignature().ToBytes()),
					Op.GetPushOp(InternalState.EscrowedCoin.Redeem.ToBytes())
				);
			return new TrustedBroadcastRequest
			{
				Key = InternalState.EscrowKey,
				Transaction = offerTransaction,
				PreviousScriptPubKey = EscrowedCoin.ScriptPubKey
			};
		}

		private TransactionSignature CreateOfferSignature()
		{
			var offerTransaction = GetUnsignedOfferTransaction();
			return offerTransaction.SignInput(InternalState.EscrowKey, InternalState.EscrowedCoin);
		}

		public SolutionKey[] GetSolutionKeys()
		{
			AssertState(SolverServerStates.WaitingEscape);
			return InternalState.SolvedPuzzles.Select(s => s.SolutionKey).ToArray();
		}

		private void AssertState(SolverServerStates state)
		{
			if(state != InternalState.Status)
				throw new InvalidOperationException("Invalid state, actual " + InternalState.Status + " while expected is " + state);
		}

		public TrustedBroadcastRequest FulfillOffer(
			TransactionSignature clientSignature,
			Script cashout,
			FeeRate feeRate)
		{
			if(clientSignature == null)
				throw new ArgumentNullException(nameof(clientSignature));
			if(feeRate == null)
				throw new ArgumentNullException(nameof(feeRate));
			AssertState(SolverServerStates.WaitingFulfillment);

			var offer = GetUnsignedOfferTransaction();
			var clientKey = AssertValidSignature(clientSignature, offer);
			offer.Inputs[0].ScriptSig = new Script(
					Op.GetPushOp(clientSignature.ToBytes()),
					Op.GetPushOp(CreateOfferSignature().ToBytes()),
					Op.GetPushOp(InternalState.EscrowedCoin.Redeem.ToBytes())
				);

			if(!offer.Inputs.AsIndexedInputs().First().VerifyScript(InternalState.EscrowedCoin))
				throw new PuzzleException("invalid-tumbler-signature");


			var solutions = InternalState.SolvedPuzzles.Select(s => s.SolutionKey).ToArray();
			var fulfill = new Transaction();
			fulfill.Inputs.Add(new TxIn());
			fulfill.Outputs.Add(new TxOut(InternalState.OfferCoin.Amount, cashout));

			var fulfillScript = SolverScriptBuilder.CreateFulfillScript(null, solutions);
			fulfill.Inputs[0].ScriptSig = fulfillScript + Op.GetPushOp(InternalState.OfferCoin.Redeem.ToBytes());
			fulfill.Outputs[0].Value -= feeRate.GetFee(fulfill.GetVirtualSize());

			InternalState.OfferClientSignature = clientSignature;
			InternalState.Status = SolverServerStates.WaitingEscape;
			return new TrustedBroadcastRequest
			{
				Key = InternalState.FulfillKey,
				PreviousScriptPubKey = InternalState.OfferCoin.ScriptPubKey,
				Transaction = fulfill
			};
		}

		private PubKey AssertValidSignature(TransactionSignature clientSignature, Transaction offer)
		{
			var escrow = EscrowScriptPubKeyParameters.GetFromCoin(InternalState.EscrowedCoin);
			var coin = InternalState.EscrowedCoin.Clone();
			coin.OverrideScriptCode(escrow.GetInitiatorScriptCode());
			var signedHash = offer.Inputs.AsIndexedInputs().First().GetSignatureHash(coin, clientSignature.SigHash);
			var clientKey = InternalState.GetClientEscrowPubKey();
			if(!clientKey.Verify(signedHash, clientSignature.Signature))
				throw new PuzzleException("invalid-client-signature");
			return clientKey;
		}

		public Transaction GetSignedEscapeTransaction(TransactionSignature clientSignature, FeeRate feeRate, Script cashout)
		{
			AssertState(SolverServerStates.WaitingEscape);
			if(clientSignature.SigHash != (SigHash.AnyoneCanPay | SigHash.None))
				throw new PuzzleException("invalid-sighash");


			var escapeTx = new Transaction();
			escapeTx.AddInput(new TxIn(InternalState.EscrowedCoin.Outpoint));
			escapeTx.AddOutput(new TxOut(InternalState.EscrowedCoin.Amount, cashout));
			escapeTx.Inputs[0].ScriptSig = new Script(
				Op.GetPushOp(TrustedBroadcastRequest.PlaceholderSignature),
				Op.GetPushOp(TrustedBroadcastRequest.PlaceholderSignature),
				Op.GetPushOp(InternalState.EscrowedCoin.Redeem.ToBytes())
				);
			escapeTx.Outputs[0].Value -= feeRate.GetFee(escapeTx);
			AssertValidSignature(clientSignature, escapeTx);

			var tumblerSignature = escapeTx.SignInput(InternalState.EscrowKey, InternalState.EscrowedCoin);
			escapeTx.Inputs[0].ScriptSig = new Script(
				Op.GetPushOp(clientSignature.ToBytes()),
				Op.GetPushOp(tumblerSignature.ToBytes()),
				Op.GetPushOp(InternalState.EscrowedCoin.Redeem.ToBytes())
				);

			if(!escapeTx.Inputs.AsIndexedInputs().First().VerifyScript(InternalState.EscrowedCoin))
				throw new PuzzleException("invalid-tumbler-signature");

			return escapeTx;
		}

		private OfferScriptPubKeyParameters CreateOfferScriptParameters()
		{
			var escrow = EscrowScriptPubKeyParameters.GetFromCoin(InternalState.EscrowedCoin);
			return new OfferScriptPubKeyParameters
			{
				Hashes = InternalState.SolvedPuzzles.Select(p => p.SolutionKey.GetHash()).ToArray(),
				FulfillKey = InternalState.FulfillKey.PubKey,
				Expiration = escrow.LockTime,
				RedeemKey = escrow.Initiator
			};
		}
	}
}
