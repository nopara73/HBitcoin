namespace HBitcoin.TumbleBit.PuzzleSolver
{
	internal class PuzzleSetElement
	{
		public Puzzle Puzzle
		{
			get;
			set;
		}

		public int Index
		{
			get;
			set;
		}

		public ServerCommitment Commitment
		{
			get;
			set;
		}
	}

	internal class RealPuzzle : PuzzleSetElement
	{
		public RealPuzzle(Puzzle puzzle, BlindFactor blindFactory)
		{
			Puzzle = puzzle;
			BlindFactor = blindFactory;
		}

		public BlindFactor BlindFactor
		{
			get; set;
		}

		public override string ToString() => "+Real " + Puzzle;
	}

	internal class FakePuzzle : PuzzleSetElement
	{
		public FakePuzzle(Puzzle puzzle, PuzzleSolution solution)
		{
			Puzzle = puzzle;
			Solution = solution;
		}

		public PuzzleSolution Solution
		{
			get;
			private set;
		}

		public override string ToString() => "-Fake " + Puzzle;
	}
}
