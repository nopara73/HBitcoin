using System;
using HBitcoin.TumbleBit.BouncyCastle.Math;

namespace HBitcoin.TumbleBit.BouncyCastle.Crypto.Parameters
{
	internal class RsaPrivateCrtKeyParameters
		: RsaKeyParameters
	{
		private readonly BigInteger e, p, q, dP, dQ, qInv;

		public RsaPrivateCrtKeyParameters(
			BigInteger modulus,
			BigInteger publicExponent,
			BigInteger privateExponent,
			BigInteger p,
			BigInteger q,
			BigInteger dP,
			BigInteger dQ,
			BigInteger qInv)
			: base(true, modulus, privateExponent)
		{
			ValidateValue(publicExponent, "publicExponent", "exponent");
			ValidateValue(p, "p", "P value");
			ValidateValue(q, "q", "Q value");
			ValidateValue(dP, "dP", "DP value");
			ValidateValue(dQ, "dQ", "DQ value");
			ValidateValue(qInv, "qInv", "InverseQ value");

			e = publicExponent;
			this.p = p;
			this.q = q;
			this.dP = dP;
			this.dQ = dQ;
			this.qInv = qInv;
		}

		public BigInteger PublicExponent => e;

		public BigInteger P => p;

		public BigInteger Q => q;

		public BigInteger DP => dP;

		public BigInteger DQ => dQ;

		public BigInteger QInv => qInv;

		public override bool Equals(
			object obj)
		{
			if(obj == this)
				return true;

			var kp = obj as RsaPrivateCrtKeyParameters;

			if (kp == null)
				return false;

			return kp.DP.Equals(dP)
				&& kp.DQ.Equals(dQ)
				&& kp.Exponent.Equals(Exponent)
				&& kp.Modulus.Equals(Modulus)
				&& kp.P.Equals(p)
				&& kp.Q.Equals(q)
				&& kp.PublicExponent.Equals(e)
				&& kp.QInv.Equals(qInv);
		}

		public override int GetHashCode() => DP.GetHashCode() ^ DQ.GetHashCode() ^ Exponent.GetHashCode() ^ Modulus.GetHashCode()
				^ P.GetHashCode() ^ Q.GetHashCode() ^ PublicExponent.GetHashCode() ^ QInv.GetHashCode();

		private static void ValidateValue(BigInteger x, string name, string desc)
		{
			if(x == null)
				throw new ArgumentNullException(name);
			if(x.SignValue <= 0)
				throw new ArgumentException("Not a valid RSA " + desc, name);
		}
	}
}