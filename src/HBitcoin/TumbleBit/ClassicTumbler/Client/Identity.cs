using System;

namespace NTumbleBit.ClassicTumbler.Client
{
	public class Identity : IEquatable<Identity>
	{
		public Role Role { get; }
		public int CycleId { get; }

		private bool _doesntMatter { get; }
		public static Identity DoesntMatter => new Identity(doesntMatter: true);

		public Identity(Role role, int cycle)
		{
			Role = role;
			CycleId = cycle;
			_doesntMatter = false;
		}

		/// <param name="doesntMatter">Only accept true</param>
		private Identity(bool doesntMatter)
		{
			if (!doesntMatter) throw new ArgumentException(nameof(doesntMatter));
			_doesntMatter = doesntMatter;

			// dummy
			Role = Role.Alice;
			CycleId = -1;
		}

		public override string ToString()
		{
			if (_doesntMatter) return "'Does not matter'";
			else return $"{Role} {CycleId}";
		}

		#region Equality
		public static bool operator ==(Identity a1, Identity a2) =>
			a1._doesntMatter
			|| a2._doesntMatter
			|| (a1.Role == a2.Role && a1.CycleId == a2.CycleId);
		public static bool operator !=(Identity a1, Identity a2) => !(a1 == a2);
		public override bool Equals(object obj) => obj is Identity && this == (Identity)obj;
		public bool Equals(Identity other) => this == other;
		public override int GetHashCode() =>
			_doesntMatter
			? _doesntMatter.GetHashCode()
			: Role.GetHashCode() ^ CycleId.GetHashCode();
		#endregion
	}
	public enum Role
	{
		Alice,
		Bob
	}
}