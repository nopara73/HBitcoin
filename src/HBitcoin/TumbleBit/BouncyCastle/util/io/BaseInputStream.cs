using System;
using System.IO;

namespace NTumbleBit.BouncyCastle.Utilities.IO
{
	internal abstract class BaseInputStream : Stream
	{
		private bool closed;

		public sealed override bool CanRead => !closed;

		public sealed override bool CanSeek => false;

		public sealed override bool CanWrite => false;

		protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closed = true;
            }
            base.Dispose(disposing);
        }
		public sealed override void Flush()
		{
		}
		public sealed override long Length
		{
			get
			{
				throw new NotSupportedException();
			}
		}
		public sealed override long Position
		{
			get
			{
				throw new NotSupportedException();
			}
			set
			{
				throw new NotSupportedException();
			}
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			var pos = offset;
			try
			{
				var end = offset + count;
				while (pos < end)
				{
					var b = ReadByte();
					if (b == -1)
						break;
					buffer[pos++] = (byte)b;
				}
			}
			catch(IOException)
			{
				if(pos == offset)
					throw;
			}
			return pos - offset;
		}

		public sealed override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}
		public sealed override void SetLength(long value)
		{
			throw new NotSupportedException();
		}
		public sealed override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}
	}
}
