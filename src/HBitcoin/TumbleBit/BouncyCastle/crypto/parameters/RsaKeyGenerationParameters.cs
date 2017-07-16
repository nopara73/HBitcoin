using HBitcoin.TumbleBit.BouncyCastle.Math;
using HBitcoin.TumbleBit.BouncyCastle.Security;

namespace HBitcoin.TumbleBit.BouncyCastle.Crypto.Parameters
{
	internal class RsaKeyGenerationParameters
		: KeyGenerationParameters
	{
		private readonly BigInteger publicExponent;
		private readonly int certainty;

		public RsaKeyGenerationParameters(
			BigInteger publicExponent,
			SecureRandom random,
			int strength,
			int certainty)
			: base(random, strength)
		{
			this.publicExponent = publicExponent;
			this.certainty = certainty;
		}

		public BigInteger PublicExponent => publicExponent;

		public int Certainty => certainty;

		public override bool Equals(
			object obj)
		{
			var other = obj as RsaKeyGenerationParameters;

			if (other == null)
			{
				return false;
			}

			return certainty == other.certainty
				&& publicExponent.Equals(other.publicExponent);
		}

		public override int GetHashCode() => certainty.GetHashCode() ^ publicExponent.GetHashCode();
	}
}