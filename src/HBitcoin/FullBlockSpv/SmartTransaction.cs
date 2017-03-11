using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace HBitcoin.FullBlockSpv
{
    public class SmartTransaction
	{
		#region Members

		// -1 if not confirmed
		public int Height { get; }
		public Transaction Transaction { get; }

		public bool Confirmed => Height != -1;
		public uint256 GetHash() => Transaction.GetHash();

		#endregion

		#region Constructors

		public SmartTransaction()
		{

		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="transaction"></param>
		/// <param name="height">Defaults to -1 if not confirmed</param>
		public SmartTransaction(Transaction transaction, int height = -1)
		{
			Height = height;
			Transaction = transaction;
		}

		#endregion
	}
}
