using NBitcoin;

namespace HBitcoin.TumbleBit.PuzzleSolver
{
	public class ServerCommitment
	{
		public ServerCommitment(uint160 keyHash, byte[] encryptedSolution)
		{
			EncryptedSolution = encryptedSolution;
			KeyHash = keyHash;
		}

		public byte[] EncryptedSolution
		{
			get; set;
		}

		public uint160 KeyHash
		{
			get; set;
		}
	}
}
