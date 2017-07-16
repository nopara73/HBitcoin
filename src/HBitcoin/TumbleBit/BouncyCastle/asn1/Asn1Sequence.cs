using System;
using System.Collections;
using System.IO;

using NTumbleBit.BouncyCastle.Utilities;

namespace NTumbleBit.BouncyCastle.Asn1
{
	internal abstract class Asn1Sequence
		: Asn1Object, IEnumerable
	{
		private readonly IList seq;

		/**
         * return an Asn1Sequence from the given object.
         *
         * @param obj the object we want converted.
         * @exception ArgumentException if the object cannot be converted.
         */
		public static Asn1Sequence GetInstance(
			object obj)
		{
			if(obj == null || obj is Asn1Sequence)
			{
				return (Asn1Sequence)obj;
			}
			else if(obj is Asn1SequenceParser)
			{
				return GetInstance(((Asn1SequenceParser)obj).ToAsn1Object());
			}
			else if(obj is byte[])
			{
				try
				{
					return GetInstance(FromByteArray((byte[])obj));
				}
				catch(IOException e)
				{
					throw new ArgumentException("failed to construct sequence from byte[]: " + e.Message);
				}
			}
			else if(obj is Asn1Encodable)
			{
				var primitive = ((Asn1Encodable)obj).ToAsn1Object();

				if (primitive is Asn1Sequence)
				{
					return (Asn1Sequence)primitive;
				}
			}

			throw new ArgumentException("Unknown object in GetInstance: " + Platform.GetTypeName(obj), nameof(obj));
		}

		protected internal Asn1Sequence(
			int capacity)
		{
			seq = Platform.CreateArrayList(capacity);
		}

		public virtual IEnumerator GetEnumerator() => seq.GetEnumerator();

		[Obsolete("Use GetEnumerator() instead")]
		public IEnumerator GetObjects() => GetEnumerator();

		private class Asn1SequenceParserImpl
			: Asn1SequenceParser
		{
			private readonly Asn1Sequence outer;
			private readonly int max;
			private int index;

			public Asn1SequenceParserImpl(
				Asn1Sequence outer)
			{
				this.outer = outer;
				max = outer.Count;
			}

			public IAsn1Convertible ReadObject()
			{
				if(index == max)
					return null;

				var obj = outer[index++];

				if (obj is Asn1Sequence)
					return ((Asn1Sequence)obj).Parser;

				// NB: Asn1OctetString implements Asn1OctetStringParser directly
				//				if (obj is Asn1OctetString)
				//					return ((Asn1OctetString)obj).Parser;

				return obj;
			}

			public Asn1Object ToAsn1Object() => outer;
		}

		public virtual Asn1SequenceParser Parser => new Asn1SequenceParserImpl(this);

		/**
         * return the object at the sequence position indicated by index.
         *
         * @param index the sequence number (starting at zero) of the object
         * @return the object at the sequence position indicated by index.
         */
		public virtual Asn1Encodable this[int index] => (Asn1Encodable)seq[index];

		[Obsolete("Use 'object[index]' syntax instead")]
		public Asn1Encodable GetObjectAt(
			int index) => this[index];

		[Obsolete("Use 'Count' property instead")]
		public int Size => Count;

		public virtual int Count => seq.Count;

		protected override int Asn1GetHashCode()
		{
			var hc = Count;

			foreach (object o in this)
			{
				hc *= 17;
				if(o == null)
				{
					hc ^= DerNull.Instance.GetHashCode();
				}
				else
				{
					hc ^= o.GetHashCode();
				}
			}

			return hc;
		}

		protected override bool Asn1Equals(
			Asn1Object asn1Object)
		{
			var other = asn1Object as Asn1Sequence;

			if (other == null)
				return false;

			if(Count != other.Count)
				return false;

			var s1 = GetEnumerator();
			var s2 = other.GetEnumerator();

			while (s1.MoveNext() && s2.MoveNext())
			{
				var o1 = GetCurrent(s1).ToAsn1Object();
				var o2 = GetCurrent(s2).ToAsn1Object();

				if(!o1.Equals(o2))
					return false;
			}

			return true;
		}

		private static Asn1Encodable GetCurrent(IEnumerator e)
		{
			var encObj = (Asn1Encodable)e.Current;

			// unfortunately null was allowed as a substitute for DER null
			if(encObj == null)
				return DerNull.Instance;

			return encObj;
		}

		protected internal void AddObject(
			Asn1Encodable obj)
		{
			seq.Add(obj);
		}
	}
}
