using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HBitcoin.MemPool
{
	public enum MemPoolState
	{
		NotStarted,
		WaitingForBlockchainSync,
		Syncing
	}
}
