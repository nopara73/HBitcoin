using NBitcoin.DataEncoders;
using HBitcoin.TumbleBit.BouncyCastle.Math;
using System;

namespace HBitcoin.TumbleBit
{
	public class PuzzleSolution
	{
		public PuzzleSolution(byte[] solution)
		{
			if(solution == null)
				throw new ArgumentNullException(nameof(solution));
			_Value = new BigInteger(1, solution);
		}

		internal PuzzleSolution(BigInteger value)
		{
			_Value = value ?? throw new ArgumentNullException(nameof(value));
		}

		internal readonly BigInteger _Value;

		public byte[] ToBytes() => _Value.ToByteArrayUnsigned();

		public override bool Equals(object obj)
		{
			var item = obj as PuzzleSolution;
			if (item == null)
				return false;
			return _Value.Equals(item._Value);
		}
		public static bool operator ==(PuzzleSolution a, PuzzleSolution b)
		{
			if(ReferenceEquals(a, b))
				return true;
			if(((object)a == null) || ((object)b == null))
				return false;
			return a._Value.Equals(b._Value);
		}

		public static bool operator !=(PuzzleSolution a, PuzzleSolution b) => !(a == b);

		public override int GetHashCode() => _Value.GetHashCode();

		public PuzzleSolution Unblind(RsaPubKey rsaPubKey, BlindFactor blind)
		{
			if(rsaPubKey == null)
				throw new ArgumentNullException(nameof(rsaPubKey));
			if(blind == null)
				throw new ArgumentNullException(nameof(blind));
			return new PuzzleSolution(rsaPubKey.Unblind(_Value, blind));
		}

		public override string ToString() => Encoders.Hex.EncodeData(ToBytes());
	}
}
