using NBitcoin;
using System;
using System.Diagnostics;
using System.Linq;

namespace HBitcoin.TumbleBit.Services
{
	public enum TransactionType : int
	{
		TumblerEscrow,
		TumblerRedeem,
		/// <summary>
		/// The transaction that cashout tumbler's escrow (go to client)
		/// </summary>
		TumblerCashout,

		ClientEscrow,
		ClientRedeem,
		ClientOffer,
		ClientEscape,
		/// <summary>
		/// The transaction that cashout client's escrow (go to tumbler)
		/// </summary>
		ClientFulfill,
		ClientOfferRedeem
	}

	public enum RecordType
	{
		Transaction,
		ScriptPubKey
	}

	public class TrackerRecord
	{
		public TrackerRecord()
		{

		}

		public int Cycle
		{
			get; set;
		}
		public RecordType RecordType
		{
			get; set;
		}
		public TransactionType TransactionType
		{
			get; set;
		}
		public uint256 TransactionId
		{
			get;
			set;
		}
		public Script ScriptPubKey
		{
			get; set;
		}
		public uint Correlation
		{
			get;
			set;
		}
	}
	public class Tracker
	{
		class InternalRecord
		{

		}
		private readonly IRepository _Repo;

		public Tracker(IRepository repo, Network network)
		{
			_Repo = repo ?? throw new ArgumentNullException(nameof(repo));
			_Network = network ?? throw new ArgumentNullException(nameof(network));
		}


		private readonly Network _Network;
		public Network Network => _Network;

		private static string GetCyclePartition(int cycleId) => "Cycle_" + cycleId;

		public void TransactionCreated(int cycleId, TransactionType type, uint256 txId, uint correlation)
		{
			var record = new TrackerRecord
			{
				Cycle = cycleId,
				RecordType = RecordType.Transaction,
				TransactionType = type,
				TransactionId = txId,
				Correlation = correlation,
			};

			var isNew = true;

			_Repo.UpdateOrInsert(GetCyclePartition(cycleId), txId.GetLow64().ToString(), record, (a, b) =>
			{
				isNew = false;
				return b;
			});
			_Repo.UpdateOrInsert("Search", "t:" + txId.ToString(), cycleId, (a, b) => b);

			if(isNew)
				Debug.WriteLine($"Tracking transaction {type} of cycle {cycleId} with correlation {correlation} ({txId})");
		}

		public void AddressCreated(int cycleId, TransactionType type, Script scriptPubKey, uint correlation)
		{
			var record = new TrackerRecord
			{
				Cycle = cycleId,
				RecordType = RecordType.ScriptPubKey,
				TransactionType = type,
				ScriptPubKey = scriptPubKey,
				Correlation = correlation
			};

			var isNew = true;
			_Repo.UpdateOrInsert(GetCyclePartition(cycleId), Rand(), record, (a, b) =>
			{
				isNew = false;
				return b;
			});
			_Repo.UpdateOrInsert("Search", "t:" + scriptPubKey.Hash.ToString(), cycleId, (a, b) => b);

			if(isNew)
				Debug.WriteLine($"Tracking address {type} of cycle {cycleId} with correlation {correlation} ({scriptPubKey.GetDestinationAddress(Network)})");
		}

		private static string Rand() => RandomUtils.GetUInt64().ToString();

		public TrackerRecord[] Search(Script script)
		{
			var row = _Repo.Get<int>("Search", "t:" + script.Hash);
			if(row == 0)
				return new TrackerRecord[0];
			return GetRecords(row).Where(r => r.RecordType == RecordType.ScriptPubKey && r.ScriptPubKey == script).ToArray();
		}

		public TrackerRecord[] Search(uint256 txId)
		{
			var row = _Repo.Get<int>("Search", "t:" + txId);
			if(row == 0)
				return new TrackerRecord[0];
			return GetRecords(row).Where(r => r.RecordType == RecordType.Transaction && r.TransactionId == txId).ToArray();
		}

		public TrackerRecord[] GetRecords(int cycleId) => _Repo.List<TrackerRecord>(GetCyclePartition(cycleId));
	}
}
