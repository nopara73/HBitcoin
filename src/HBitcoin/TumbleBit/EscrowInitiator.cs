using NBitcoin;
using NTumbleBit.PuzzleSolver;
using System;
using NTumbleBit.ClassicTumbler;

namespace NTumbleBit
{
	public interface IEscrow
	{
		ScriptCoin EscrowedCoin
		{
			get;
		}
	}
    public abstract class EscrowInitiator : IEscrow
    {
		public class State
		{
			public ScriptCoin EscrowedCoin
			{
				get;
				set;
			}
			public Key EscrowKey
			{
				get;
				set;
			}
			public Script RedeemDestination
			{
				get;
				set;
			}
		}

		protected State InternalState
		{
			get; set;
		}

		public virtual void ConfigureEscrowedCoin(ScriptCoin escrowedCoin, Key escrowKey, Script redeemDestination)
		{
			if(escrowedCoin == null)
				throw new ArgumentNullException(nameof(escrowedCoin));
			if(escrowKey == null)
				throw new ArgumentNullException(nameof(escrowKey));
			var escrow = EscrowScriptPubKeyParameters.GetFromCoin(escrowedCoin);
			if(escrow == null ||
				escrow.Initiator != escrowKey.PubKey)
				throw new PuzzleException("Invalid escrow");
			InternalState.EscrowedCoin = escrowedCoin;
			InternalState.EscrowKey = escrowKey;
			InternalState.RedeemDestination = redeemDestination ?? throw new ArgumentNullException(nameof(redeemDestination));
		}

		public TrustedBroadcastRequest CreateRedeemTransaction(FeeRate feeRate)
		{
			if(feeRate == null)
				throw new ArgumentNullException(nameof(feeRate));

			var escrow = EscrowScriptPubKeyParameters.GetFromCoin(InternalState.EscrowedCoin);
			var escrowCoin = InternalState.EscrowedCoin;
			var tx = new Transaction
			{
				LockTime = escrow.LockTime
			};
			tx.Inputs.Add(new TxIn());
			//Put a dummy signature and the redeem script
			tx.Inputs[0].ScriptSig =
				new Script(
					Op.GetPushOp(TrustedBroadcastRequest.PlaceholderSignature),
					Op.GetPushOp(escrowCoin.Redeem.ToBytes()));
			tx.Inputs[0].Sequence = 0;

			tx.Outputs.Add(new TxOut(escrowCoin.Amount, InternalState.RedeemDestination));
			tx.Outputs[0].Value -= feeRate.GetFee(tx.GetVirtualSize());

			var redeemTransaction =  new TrustedBroadcastRequest
			{
				Key = InternalState.EscrowKey,
				PreviousScriptPubKey = escrowCoin.ScriptPubKey,
				Transaction = tx
			};
			return redeemTransaction;
		}

		public abstract LockTime GetLockTime(CycleParameters cycle);

		public string Id => InternalState.EscrowedCoin.ScriptPubKey.ToHex();

		public ScriptCoin EscrowedCoin => InternalState.EscrowedCoin;
	}
}
