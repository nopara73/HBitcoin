using System;

namespace NTumbleBit.Configuration
{
	public class ConfigException : Exception
	{
		public ConfigException():base("")
		{

		}
		public ConfigException(string message) : base(message)
		{

		}
	}
}
