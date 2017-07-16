using NBitcoin;
using HBitcoin.TumbleBit.BouncyCastle.Security;

namespace HBitcoin.TumbleBit
{
	internal class NBitcoinSecureRandom : SecureRandom
	{

		private static readonly NBitcoinSecureRandom _Instance = new NBitcoinSecureRandom();
		public static NBitcoinSecureRandom Instance => _Instance;

		private NBitcoinSecureRandom()
		{

		}

		public override void NextBytes(byte[] buffer)
		{
			RandomUtils.GetBytes(buffer);
		}

	}
}
