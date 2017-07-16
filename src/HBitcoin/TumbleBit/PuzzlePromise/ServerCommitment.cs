namespace HBitcoin.TumbleBit.PuzzlePromise
{
	public class ServerCommitment
    {
		public ServerCommitment(PuzzleValue puzzleValue, byte[] promise)
		{
			Puzzle = puzzleValue;
			Promise = promise;
		}
		public ServerCommitment()
		{

		}

		public PuzzleValue Puzzle
		{
			get; set;
		}
		public byte[] Promise
		{
			get; set;
		}
	}
}
