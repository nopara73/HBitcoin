namespace NTumbleBit.BouncyCastle.Asn1
{
	/**
	 * A Null object.
	 */
	internal class DerNull
		: Asn1Null
	{
		public static readonly DerNull Instance = new DerNull();

		private byte[] zeroBytes = new byte[0];

		private DerNull()
		{
		}

		internal override void Encode(
			DerOutputStream derOut)
		{
			derOut.WriteEncoded(Asn1Tags.Null, zeroBytes);
		}

		protected override bool Asn1Equals(
			Asn1Object asn1Object) => asn1Object is DerNull;

		protected override int Asn1GetHashCode() => -1;
	}
}
