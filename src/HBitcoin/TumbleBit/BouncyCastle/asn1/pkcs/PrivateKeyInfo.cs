
using HBitcoin.TumbleBit.BouncyCastle.Asn1.X509;

namespace HBitcoin.TumbleBit.BouncyCastle.Asn1.Pkcs
{
	internal class PrivateKeyInfo
		: Asn1Encodable
	{
		private readonly Asn1Object privKey;
		private readonly AlgorithmIdentifier algID;

		public PrivateKeyInfo(
			AlgorithmIdentifier algID,
			Asn1Object privateKey)
		{
			privKey = privateKey;
			this.algID = algID;
		}

		public AlgorithmIdentifier AlgorithmID => algID;

		public Asn1Object PrivateKey => privKey;

		/**
         * write out an RSA private key with its associated information
         * as described in Pkcs8.
         * <pre>
         *      PrivateKeyInfo ::= Sequence {
         *                              version Version,
         *                              privateKeyAlgorithm AlgorithmIdentifier {{PrivateKeyAlgorithms}},
         *                              privateKey PrivateKey,
         *                              attributes [0] IMPLICIT Attributes OPTIONAL
         *                          }
         *      Version ::= Integer {v1(0)} (v1,...)
         *
         *      PrivateKey ::= OCTET STRING
         *
         *      Attributes ::= Set OF Attr
         * </pre>
         */
		public override Asn1Object ToAsn1Object()
		{
			var v = new Asn1EncodableVector(
				new DerInteger(0),
				algID,
				new DerOctetString(privKey.GetEncoded()));
			return new DerSequence(v);
		}
	}
}