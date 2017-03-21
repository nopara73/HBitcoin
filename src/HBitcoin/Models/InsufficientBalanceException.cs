using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HBitcoin.Models
{
	public class InsufficientBalanceException : Exception
	{
		public InsufficientBalanceException()
		{

		}
	}
}
