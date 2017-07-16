namespace HBitcoin.TumbleBit.PuzzleSolver
{
	public class SolverClientRevelation
	{
		public SolverClientRevelation()
		{

		}
		public SolverClientRevelation(int[] fakeIndexes, PuzzleSolution[] solutions)
		{
			FakeIndexes = fakeIndexes;
			Solutions = solutions;
		}

		public int[] FakeIndexes
		{
			get; set;
		}

		public PuzzleSolution[] Solutions
		{
			get; set;
		}
	}
}
