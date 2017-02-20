using System;

namespace HBitcoin.KeyManagement
{
	public enum HdPathType
	{
		Stealth,
		Receive,
		Change,
		NonHardened
	}

	public static class Hierarchy
	{
		public static string GetPathString(HdPathType type)
		{
			switch(type)
			{
				case HdPathType.Stealth:
					return "0'";
				case HdPathType.Receive:
					return "1'";
				case HdPathType.Change:
					return "2'";
				case HdPathType.NonHardened:
					return "3";
				default:
					throw new ArgumentOutOfRangeException(nameof(type), type, null);
			}
		}
	}
}
