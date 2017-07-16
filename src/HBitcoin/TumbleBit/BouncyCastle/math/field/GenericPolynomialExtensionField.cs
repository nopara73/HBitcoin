using System;

namespace NTumbleBit.BouncyCastle.Math.Field
{
	internal class GenericPolynomialExtensionField
		: IPolynomialExtensionField
	{
		protected readonly IFiniteField subfield;
		protected readonly IPolynomial minimalPolynomial;

		internal GenericPolynomialExtensionField(IFiniteField subfield, IPolynomial polynomial)
		{
			this.subfield = subfield;
			minimalPolynomial = polynomial;
		}

		public virtual BigInteger Characteristic => subfield.Characteristic;

		public virtual int Dimension => subfield.Dimension * minimalPolynomial.Degree;

		public virtual IFiniteField Subfield => subfield;

		public virtual int Degree => minimalPolynomial.Degree;

		public virtual IPolynomial MinimalPolynomial => minimalPolynomial;

		public override bool Equals(object obj)
		{
			if(this == obj)
			{
				return true;
			}
			var other = obj as GenericPolynomialExtensionField;
			if (null == other)
			{
				return false;
			}
			return subfield.Equals(other.subfield) && minimalPolynomial.Equals(other.minimalPolynomial);
		}

		public override int GetHashCode()
		{
			throw new NotImplementedException();
		}
	}
}
