using NTumbleBit.BouncyCastle.Utilities;

namespace NTumbleBit.BouncyCastle.Math.Field
{
	internal class GF2Polynomial
		: IPolynomial
	{
		protected readonly int[] exponents;

		internal GF2Polynomial(int[] exponents)
		{
			this.exponents = Arrays.Clone(exponents);
		}

		public virtual int Degree => exponents[exponents.Length - 1];

		public virtual int[] GetExponentsPresent() => Arrays.Clone(exponents);

		public override bool Equals(object obj)
		{
			if(this == obj)
			{
				return true;
			}
			var other = obj as GF2Polynomial;
			if (null == other)
			{
				return false;
			}
			return Arrays.AreEqual(exponents, other.exponents);
		}

		public override int GetHashCode() => Arrays.GetHashCode(exponents);
	}
}
