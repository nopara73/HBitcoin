using System;

namespace HBitcoin.TumbleBit.BouncyCastle.Security
{
	internal class KeyException : GeneralSecurityException
	{
		public KeyException() : base() { }
		public KeyException(string message) : base(message) { }
		public KeyException(string message, Exception exception) : base(message, exception) { }
	}
}
