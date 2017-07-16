using System.Threading.Tasks;

namespace NTumbleBit
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
