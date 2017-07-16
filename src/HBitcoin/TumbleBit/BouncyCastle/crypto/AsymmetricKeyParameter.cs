namespace NTumbleBit.BouncyCastle.Crypto
{
	internal abstract class AsymmetricKeyParameter
		: ICipherParameters
	{
		private readonly bool privateKey;

		protected AsymmetricKeyParameter(
			bool privateKey)
		{
			this.privateKey = privateKey;
		}

		public bool IsPrivate => privateKey;

		public override bool Equals(
			object obj)
		{
			var other = obj as AsymmetricKeyParameter;

			if (other == null)
			{
				return false;
			}

			return Equals(other);
		}

		protected bool Equals(
			AsymmetricKeyParameter other) => privateKey == other.privateKey;

		public override int GetHashCode() => privateKey.GetHashCode();
	}
}
