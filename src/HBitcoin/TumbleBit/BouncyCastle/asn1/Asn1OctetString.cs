using System;
using System.IO;

using NTumbleBit.BouncyCastle.Utilities;

namespace NTumbleBit.BouncyCastle.Asn1
{
	internal abstract class Asn1OctetString
		: Asn1Object, Asn1OctetStringParser
	{
		internal byte[] str;

		/**
         * @param string the octets making up the octet string.
         */
		internal Asn1OctetString(
			byte[] str)
		{
			this.str = str ?? throw new ArgumentNullException(nameof(str));
		}

		public Stream GetOctetStream() => new MemoryStream(str, false);

		public Asn1OctetStringParser Parser => this;

		public virtual byte[] GetOctets() => str;

		protected override int Asn1GetHashCode() => Arrays.GetHashCode(GetOctets());

		protected override bool Asn1Equals(
			Asn1Object asn1Object)
		{
			var other = asn1Object as DerOctetString;

			if(other == null)
				return false;

			return Arrays.AreEqual(GetOctets(), other.GetOctets());
		}
	}
}
