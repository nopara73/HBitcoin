using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NBitcoin;

namespace HBitcoin.KeyManagement
{
	public enum HdPathType
	{
		Stealth,
		Receive,
		Change,
		NonHardened,
		Account // special
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
				case HdPathType.Account:
					return "4'";  // special
				default:
					throw new ArgumentOutOfRangeException(nameof(type), type, null);
			}
		}

		public static string GetPathString(SafeAccount account) => account.PathString;
	}

	public class SafeAccount
	{
		public readonly uint Id;
		public readonly string PathString;

		public SafeAccount(uint id)
		{
			try
			{
				string firstPart = Hierarchy.GetPathString(HdPathType.Account);

				string lastPart = $"/{id}'";
				PathString = firstPart + lastPart;

				KeyPath.Parse(PathString);
			}
			catch (Exception ex)
			{
				throw new ArgumentOutOfRangeException($"{nameof(id)} : {id}", ex);
			}

			Id = id;
		}
	}
}
