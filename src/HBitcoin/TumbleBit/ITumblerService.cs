using HBitcoin.KeyManagement;
using System.Threading.Tasks;

namespace HBitcoin.TumbleBit
{
	public interface ITumblerService
	{
		string Name
		{
			get;
		}
		void Start(SafeAccount inputAccount, SafeAccount outputAccount);
		Task Stop();
		bool Started
		{
			get;
		}
	}
}
