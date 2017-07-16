namespace NTumbleBit.BouncyCastle.Math.Field
{
	internal class PrimeField
		: IFiniteField
	{
		protected readonly BigInteger characteristic;

		internal PrimeField(BigInteger characteristic)
		{
			this.characteristic = characteristic;
		}

		public virtual BigInteger Characteristic => characteristic;

		public virtual int Dimension => 1;

		public override bool Equals(object obj)
		{
			if(this == obj)
			{
				return true;
			}
			var other = obj as PrimeField;
			if (null == other)
			{
				return false;
			}
			return characteristic.Equals(other.characteristic);
		}

		public override int GetHashCode() => characteristic.GetHashCode();
	}
}
