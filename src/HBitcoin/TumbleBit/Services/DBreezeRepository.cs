using DBreeze;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NTumbleBit.Services
{
	public class DBreezeRepository : IRepository, IDisposable
	{
		private string _Folder;
		public DBreezeRepository(string folder)
		{
			if(folder == null)
				throw new ArgumentNullException(nameof(folder));
			if(!Directory.Exists(folder))
				Directory.CreateDirectory(folder);
			_Folder = folder;
		}

		private Dictionary<string, DBreezeEngineReference> _EnginesByParitionKey = new Dictionary<string, DBreezeEngineReference>();

		public void UpdateOrInsert<T>(string partitionKey, string rowKey, T data, Func<T, T, T> update)
		{
			lock(_EnginesByParitionKey)
			{
				var engine = GetEngine(partitionKey);
				using(var tx = engine.GetTransaction())
				{
					var newValue = data;
					var existingRow = tx.Select<string, byte[]>(GetTableName<T>(), rowKey);
					if(existingRow != null && existingRow.Exists)
					{
						var existing = Serializer.ToObject<T>(Unzip(existingRow.Value));
						if(existing != null)
							newValue = update(existing, newValue);
					}
					tx.Insert(GetTableName<T>(), rowKey, Zip(Serializer.ToString(newValue)));
					tx.Commit();
				}
			}
		}

		private static byte[] Zip(string unzipped)
		{
			var ms = new MemoryStream();
			using (GZipStream gzip = new GZipStream(ms, CompressionMode.Compress))
			using (var writer = new StreamWriter(gzip, Encoding.UTF8))
			{
				writer.Write(unzipped);
				writer.Flush();
				return ms.ToArray();
			}
		}

		private static string Unzip(byte[] bytes)
		{
			var ms = new MemoryStream(bytes);
			using (GZipStream gzip = new GZipStream(ms, CompressionMode.Decompress))
			using (var reader = new StreamReader(gzip, Encoding.UTF8))
			{
				var unzipped = reader.ReadToEnd();
				return unzipped;
			}
		}

		private DBreezeEngine GetEngine(string partitionKey)
		{
			if(!Directory.Exists(_Folder))
				Directory.CreateDirectory(_Folder);
			var partitionPath = GetPartitionPath(partitionKey);
			if (!Directory.Exists(partitionPath))
				Directory.CreateDirectory(partitionPath);
			if (!_EnginesByParitionKey.TryGetValue(partitionKey, out DBreezeEngineReference engine))
			{
				engine = new DBreezeEngineReference { PartitionKey = partitionKey, Engine = new DBreezeEngine(partitionPath) };
				_EnginesByParitionKey.Add(partitionKey, engine);
				_EngineReferences.Enqueue(engine);
			}
			engine.Used++;
			while(_EngineReferences.Count > MaxOpenedEngine)
			{
				var reference = _EngineReferences.Dequeue();
				reference.Used--;
				if(reference.Used <= 0 && reference != engine)
				{
					if(_EnginesByParitionKey.Remove(reference.PartitionKey))
						reference.Engine.Dispose();
				}
				else
				{
					_EngineReferences.Enqueue(reference);
				}
			}
			return engine.Engine;
		}

		Queue<DBreezeEngineReference> _EngineReferences = new Queue<DBreezeEngineReference>();

		public int OpenedEngine
		{
			get
			{
				lock(_EnginesByParitionKey)
				{
					return _EngineReferences.Count;
				}
			}
		}
		public int MaxOpenedEngine
		{
			get;
			set;
		} = 10;

		class DBreezeEngineReference
		{
			public DBreezeEngine Engine
			{
				get; set;
			}
			public string PartitionKey
			{
				get;
				internal set;
			}
			public int Used
			{
				get; set;
			}
		}
		private string GetPartitionPath(string partitionKey) => Path.Combine(_Folder, GetDirectory(partitionKey));

		private static string GetDirectory(string partitionKey) => partitionKey;

		public void Delete(string partitionKey)
		{
			lock(_EnginesByParitionKey)
			{
				if(!_EnginesByParitionKey.ContainsKey(partitionKey))
					return;

				var engine = GetEngine(partitionKey);
				engine.Dispose();
				_EnginesByParitionKey.Remove(partitionKey);
				Utils.DeleteRecursivelyWithMagicDustAsync(GetPartitionPath(partitionKey)).Wait();
			}
		}

		public T[] List<T>(string partitionKey)
		{
			lock(_EnginesByParitionKey)
			{
				var result = new List<T>();
				var engine = GetEngine(partitionKey);
				using(var tx = engine.GetTransaction())
				{
					foreach(var row in tx.SelectForward<string, byte[]>(GetTableName<T>()))
					{
						result.Add(Serializer.ToObject<T>(Unzip(row.Value)));
					}
				}
				return result.ToArray();
			}
		}

		private static string GetTableName<T>() => typeof(T).FullName;

		public T Get<T>(string partitionKey, string rowKey)
		{
			lock(_EnginesByParitionKey)
			{
				var engine = GetEngine(partitionKey);
				using(var tx = engine.GetTransaction())
				{
					return Get<T>(rowKey, tx);
				}
			}
		}

		private static T Get<T>(string rowKey, DBreeze.Transactions.Transaction tx)
		{
			var row = tx.Select<string, byte[]>(GetTableName<T>(), rowKey);
			if(row == null || !row.Exists)
				return default(T);
			try
			{
				return Serializer.ToObject<T>(Unzip(row.Value));
			}
			catch { return default(T); }
		}

		public bool Delete<T>(string partitionKey, string rowKey)
		{
			lock(_EnginesByParitionKey)
			{
				var removed = false;
				var engine = GetEngine(partitionKey);
				using(var tx = engine.GetTransaction())
				{
					tx.RemoveKey(GetTableName<T>(), rowKey, out removed);
					tx.Commit();
				}
				return removed;
			}
		}

		public void Dispose()
		{
			lock(_EnginesByParitionKey)
			{
				foreach(var engine in _EnginesByParitionKey)
				{
					engine.Value.Engine.Dispose();
				}
				_EngineReferences.Clear();
				_EnginesByParitionKey.Clear();
			}
		}
	}
}
