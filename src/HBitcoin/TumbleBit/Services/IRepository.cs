using System;

namespace NTumbleBit.Services
{
	public interface IRepository
	{
		void UpdateOrInsert<T>(string partitionKey, string rowKey, T data, Func<T, T, T> update);
		T[] List<T>(string partitionKey);
		bool Delete<T>(string partitionKey, string rowKey);
		void Delete(string partitionKey);
		T Get<T>(string partitionKey, string rowKey);
	}
}
