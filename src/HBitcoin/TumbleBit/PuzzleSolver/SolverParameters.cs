using System;

namespace NTumbleBit.PuzzleSolver
{
	public class SolverParameters
	{
		public SolverParameters()
		{
			FakePuzzleCount = 285;
			RealPuzzleCount = 15;
		}

		public SolverParameters(RsaPubKey serverKey) : this()
		{
			ServerKey = serverKey ?? throw new ArgumentNullException(nameof(serverKey));
		}


		public RsaPubKey ServerKey
		{
			get; set;
		}
		public int FakePuzzleCount
		{
			get; set;
		}
		public int RealPuzzleCount
		{
			get; set;
		}

		public int GetTotalCount() => RealPuzzleCount + FakePuzzleCount;
	}
}
