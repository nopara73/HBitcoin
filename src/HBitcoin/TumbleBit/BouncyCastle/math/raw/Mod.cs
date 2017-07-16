using System;
using System.Diagnostics;

using NTumbleBit.BouncyCastle.Crypto.Utilities;
using NTumbleBit.BouncyCastle.Security;

namespace NTumbleBit.BouncyCastle.Math.Raw
{
	internal abstract class Mod
	{
		private static readonly SecureRandom RandomSource = new SecureRandom();

		public static void Invert(uint[] p, uint[] x, uint[] z)
		{
			var len = p.Length;
			if (Nat.IsZero(len, x))
				throw new ArgumentException("cannot be 0", nameof(x));
			if(Nat.IsOne(len, x))
			{
				Array.Copy(x, 0, z, 0, len);
				return;
			}

			var u = Nat.Copy(len, x);
			var a = Nat.Create(len);
			a[0] = 1;
			var ac = 0;

			if ((u[0] & 1) == 0)
			{
				InversionStep(p, u, len, a, ref ac);
			}
			if(Nat.IsOne(len, u))
			{
				InversionResult(p, ac, a, z);
				return;
			}

			var v = Nat.Copy(len, p);
			var b = Nat.Create(len);
			var bc = 0;

			var uvLen = len;

			for (;;)
			{
				while(u[uvLen - 1] == 0 && v[uvLen - 1] == 0)
				{
					--uvLen;
				}

				if(Nat.Gte(len, u, v))
				{
					Nat.SubFrom(len, v, u);
					Debug.Assert((u[0] & 1) == 0);
					ac += Nat.SubFrom(len, b, a) - bc;
					InversionStep(p, u, uvLen, a, ref ac);
					if(Nat.IsOne(len, u))
					{
						InversionResult(p, ac, a, z);
						return;
					}
				}
				else
				{
					Nat.SubFrom(len, u, v);
					Debug.Assert((v[0] & 1) == 0);
					bc += Nat.SubFrom(len, a, b) - ac;
					InversionStep(p, v, uvLen, b, ref bc);
					if(Nat.IsOne(len, v))
					{
						InversionResult(p, bc, b, z);
						return;
					}
				}
			}
		}

		public static uint[] Random(uint[] p)
		{
			var len = p.Length;
			var s = Nat.Create(len);

			var m = p[len - 1];
			m |= m >> 1;
			m |= m >> 2;
			m |= m >> 4;
			m |= m >> 8;
			m |= m >> 16;

			do
			{
				var bytes = new byte[len << 2];
				RandomSource.NextBytes(bytes);
				Pack.BE_To_UInt32(bytes, 0, s);
				s[len - 1] &= m;
			}
			while(Nat.Gte(len, s, p));

			return s;
		}

		public static void Add(uint[] p, uint[] x, uint[] y, uint[] z)
		{
			var len = p.Length;
			var c = Nat.Add(len, x, y, z);
			if (c != 0)
			{
				Nat.SubFrom(len, p, z);
			}
		}

		public static void Subtract(uint[] p, uint[] x, uint[] y, uint[] z)
		{
			var len = p.Length;
			var c = Nat.Sub(len, x, y, z);
			if (c != 0)
			{
				Nat.AddTo(len, p, z);
			}
		}

		private static void InversionResult(uint[] p, int ac, uint[] a, uint[] z)
		{
			if(ac < 0)
			{
				Nat.Add(p.Length, a, p, z);
			}
			else
			{
				Array.Copy(a, 0, z, 0, p.Length);
			}
		}

		private static void InversionStep(uint[] p, uint[] u, int uLen, uint[] x, ref int xc)
		{
			var len = p.Length;
			var count = 0;
			while (u[0] == 0)
			{
				Nat.ShiftDownWord(uLen, u, 0);
				count += 32;
			}

			{
				var zeroes = GetTrailingZeroes(u[0]);
				if (zeroes > 0)
				{
					Nat.ShiftDownBits(uLen, u, zeroes, 0);
					count += zeroes;
				}
			}

			for(int i = 0; i < count; ++i)
			{
				if((x[0] & 1) != 0)
				{
					if(xc < 0)
					{
						xc += (int)Nat.AddTo(len, p, x);
					}
					else
					{
						xc += Nat.SubFrom(len, p, x);
					}
				}

				Debug.Assert(xc == 0 || xc == -1);
				Nat.ShiftDownBit(len, x, (uint)xc);
			}
		}

		private static int GetTrailingZeroes(uint x)
		{
			Debug.Assert(x != 0);
			var count = 0;
			while ((x & 1) == 0)
			{
				x >>= 1;
				++count;
			}
			return count;
		}
	}
}
