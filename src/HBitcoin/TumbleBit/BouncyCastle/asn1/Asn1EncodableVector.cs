using System;
using System.Collections;

using NTumbleBit.BouncyCastle.Utilities;

namespace NTumbleBit.BouncyCastle.Asn1
{
	internal class Asn1EncodableVector
		: IEnumerable
	{
		private IList v = Platform.CreateArrayList();

		public static Asn1EncodableVector FromEnumerable(
			IEnumerable e)
		{
			var v = new Asn1EncodableVector();
			foreach(Asn1Encodable obj in e)
			{
				v.Add(obj);
			}
			return v;
		}

		//		public Asn1EncodableVector()
		//		{
		//		}

		public Asn1EncodableVector(
			params Asn1Encodable[] v)
		{
			Add(v);
		}

		//		public void Add(
		//			Asn1Encodable obj)
		//		{
		//			v.Add(obj);
		//		}

		public void Add(
			params Asn1Encodable[] objs)
		{
			foreach(Asn1Encodable obj in objs)
			{
				v.Add(obj);
			}
		}

		public void AddOptional(
			params Asn1Encodable[] objs)
		{
			if(objs != null)
			{
				foreach(Asn1Encodable obj in objs)
				{
					if(obj != null)
					{
						v.Add(obj);
					}
				}
			}
		}

		public Asn1Encodable this[
			int index] => (Asn1Encodable)v[index];

		[Obsolete("Use 'object[index]' syntax instead")]
		public Asn1Encodable Get(
			int index) => this[index];

		[Obsolete("Use 'Count' property instead")]
		public int Size => v.Count;

		public int Count => v.Count;

		public IEnumerator GetEnumerator() => v.GetEnumerator();
	}
}
