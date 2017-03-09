using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace HBitcoin.FullBlockSpv
{
    public class UnprocessedBlockBuffer
    {
	    public const int Capacity = 50;
		// int is the height
		private readonly ConcurrentObservableDictionary<int, Block> _blocks = new ConcurrentObservableDictionary<int, Block>();

		public event EventHandler HaveBlocks;
		private void OnHaveBlocks() => HaveBlocks?.Invoke(this, EventArgs.Empty);

		/// <summary>
		/// 
		/// </summary>
		/// <param name="height"></param>
		/// <param name="block"></param>
		/// <returns>false if we have more than UnprocessedBlockBuffer.Capacity blocks in memory already</returns>
		public bool TryAddOrReplace(int height, Block block)
	    {
			if (_blocks.Count > Capacity) return false;
			
		    _blocks.AddOrReplace(height, block);

			if (_blocks.Count == 1) OnHaveBlocks();
			return true;
	    }

	    public bool Full => _blocks.Count == Capacity;
		/// <summary>
		/// -1 if empty
		/// </summary>
	    public int BestHeight => _blocks.Count == 0 ? -1 : _blocks.Keys.Max();

	    /// <summary>
	    /// 
	    /// </summary>
	    /// <returns>false if empty</returns>
	    public bool TryGetAndRemoveOldest(out int height, out Block block)
	    {
		    height = default(int);
		    block = default(Block);
		    if(_blocks.Count == 0) return false;

			height = _blocks.Keys.Min();
			block = _blocks[height];
			_blocks.Remove(height);

			return true;
		}
	}
}
