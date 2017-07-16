using NBitcoin;
using System;

namespace HBitcoin.TumbleBit.BouncyCastle.Security
{
	internal class SecureRandom : Random
	{
		public SecureRandom()
		{
		}

		public override int Next() => RandomUtils.GetInt32();

		public override int Next(int maxValue)
		{
			throw new NotImplementedException();
		}

		public override int Next(int minValue, int maxValue)
		{
			throw new NotImplementedException();
		}

		public override void NextBytes(byte[] buffer)
		{
			RandomUtils.GetBytes(buffer);
		}
	}
}
