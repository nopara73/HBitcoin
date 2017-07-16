using System.IO;

using HBitcoin.TumbleBit.BouncyCastle.Utilities.IO;

namespace HBitcoin.TumbleBit.BouncyCastle.Asn1
{
	internal abstract class LimitedInputStream
		: BaseInputStream
	{
		protected readonly Stream _in;
		private int _limit;

		internal LimitedInputStream(
			Stream inStream,
			int limit)
		{
			_in = inStream;
			_limit = limit;
		}

		internal virtual int GetRemaining() => _limit;

		protected virtual void SetParentEofDetect(bool on)
		{
		}
	}
}
