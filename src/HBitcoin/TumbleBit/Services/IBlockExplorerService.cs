using System.Threading.Tasks;
using NBitcoin;
using System.Threading;

namespace NTumbleBit.Services
{
	public class TransactionInformation
	{
		public int Confirmations
		{
			get; set;
		}
		public MerkleBlock MerkleProof
		{
			get;
			set;
		}
		public Transaction Transaction
		{
			get; set;
		}
	}
    public interface IBlockExplorerService
    {
		int GetCurrentHeight();
		TransactionInformation[] GetTransactions(Script scriptPubKey, bool withProof);
		TransactionInformation GetTransaction(uint256 txId);
		Task<uint256> WaitBlockAsync(uint256 currentBlock, CancellationToken cancellation);
		void Track(Script scriptPubkey);
		int GetBlockConfirmations(uint256 blockId);
		bool TrackPrunedTransaction(Transaction transaction, MerkleBlock merkleProof);
	}
}
