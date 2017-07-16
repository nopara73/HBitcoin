using System.Threading.Tasks;

namespace HBitcoin.TumbleBit
{
	public interface ITumblerService
	{
		string Name
		{
			get;
		}
		void Start();
		Task Stop();
		bool Started
		{
			get;
		}
	}
}
