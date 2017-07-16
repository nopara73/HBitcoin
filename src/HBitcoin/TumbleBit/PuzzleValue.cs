using NBitcoin.DataEncoders;
using NTumbleBit.BouncyCastle.Math;
using System;

namespace NTumbleBit
{
	public class PuzzleValue
	{
		internal readonly BigInteger _Value;

		public PuzzleValue(byte[] z)
		{
			if(z == null)
				throw new ArgumentNullException(nameof(z));
			_Value = new BigInteger(1, z);
		}
		internal PuzzleValue(BigInteger z)
		{
			_Value = z ?? throw new ArgumentNullException(nameof(z));
		}

		public byte[] ToBytes() => _Value.ToByteArrayUnsigned();

		public override bool Equals(object obj)
		{
			var item = obj as PuzzleValue;
			if (item == null)
				return false;
			return _Value.Equals(item._Value);
		}
		public static bool operator ==(PuzzleValue a, PuzzleValue b)
		{
			if(ReferenceEquals(a, b))
				return true;
			if(((object)a == null) || ((object)b == null))
				return false;
			return a._Value.Equals(b._Value);
		}

		public PuzzleSolution Solve(RsaKey key)
		{
			if(key == null)
				throw new ArgumentNullException(nameof(key));
			return key.SolvePuzzle(this);
		}

		public static bool operator !=(PuzzleValue a, PuzzleValue b) => !(a == b);

		public override int GetHashCode() => _Value.GetHashCode();

		public override string ToString() => Encoders.Hex.EncodeData(ToBytes());

		public Puzzle WithRsaKey(RsaPubKey key) => new Puzzle(key, this);
	}
}
