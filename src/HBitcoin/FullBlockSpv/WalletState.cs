using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HBitcoin.FullBlockSpv
{
	public enum WalletState
	{
		NotStarted,
		SyncingBlocks,
		SyncingMempool // MemPool is never fully synced, it's always changing
	}
}
