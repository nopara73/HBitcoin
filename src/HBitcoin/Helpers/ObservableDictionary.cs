//from https://github.com/brianchance/MonoTouchMVVMCrossValidationTester/blob/master/Validation.Core/ObservableDictionary.cs
//modified

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace System.Collections.ObjectModel
{
	public class ObservableDictionary<TKey, TValue> : IDictionary<TKey, TValue>, INotifyCollectionChanged, INotifyPropertyChanged
	{
		private const string CountString = "Count";
		private const string IndexerName = "Item[]";
		private const string KeysName = "Keys";
		private const string ValuesName = "Values";

		private readonly object Lock = new object();

		protected ConcurrentDictionary<TKey, TValue> Dictionary { get; private set; }

		#region Constructors
		public ObservableDictionary()
		{
			Dictionary = new ConcurrentDictionary<TKey, TValue>();
		}
		public ObservableDictionary(ConcurrentDictionary<TKey, TValue> dictionary)
		{
			Dictionary = new ConcurrentDictionary<TKey, TValue>(dictionary);
		}
		public ObservableDictionary(IEqualityComparer<TKey> comparer)
		{
			Dictionary = new ConcurrentDictionary<TKey, TValue>(comparer);
		}
		public ObservableDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
		{
			Dictionary = new ConcurrentDictionary<TKey, TValue>(dictionary, comparer);
		}
		#endregion

		#region IDictionary<TKey,TValue> Members

		public void Add(TKey key, TValue value) => Insert(key, value, true);

		public bool ContainsKey(TKey key) => Dictionary.ContainsKey(key);

		public ICollection<TKey> Keys => Dictionary.Keys;

		public bool Remove(TKey key) => Remove(key, suppressNotifications: false);

		private bool Remove(TKey key, bool suppressNotifications)
		{
			lock(Lock)
			{
				TValue value;
				var ret = Dictionary.TryRemove(key, out value);
				if(ret && !suppressNotifications) OnCollectionChanged();
				return ret;
			}
		}

		public bool TryGetValue(TKey key, out TValue value) => Dictionary.TryGetValue(key, out value);

		public ICollection<TValue> Values => Dictionary.Values;

		public TValue this[TKey key]
		{
			get
			{
				TValue value;
				return TryGetValue(key, out value) ? value : default(TValue);
			}
			set
			{
				Insert(key, value, false);
			}
		}

		#endregion

		#region ICollection<KeyValuePair<TKey,TValue>> Members

		public void Add(KeyValuePair<TKey, TValue> item) => Insert(item.Key, item.Value, true);

		public void Clear()
		{
			lock(Lock)
			{
				if (Dictionary.Count > 0)
				{
					Dictionary.Clear();
					OnCollectionChanged();
				}
			}
		}

		public bool Contains(KeyValuePair<TKey, TValue> item) => Dictionary.Contains(item);

		/// <summary>
		/// NotImplementedException
		/// </summary>
		/// <param name="array"></param>
		/// <param name="arrayIndex"></param>
		public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
		{
			throw new NotImplementedException();
		}
		
		/// <summary>
		/// NotImplementedException
		/// </summary>
		public bool IsReadOnly
		{
			get { throw new NotImplementedException(); }
		}

		public int Count => Dictionary.Count;

		public bool Remove(KeyValuePair<TKey, TValue> item) => Remove(item.Key);

		#endregion

		#region IEnumerable<KeyValuePair<TKey,TValue>> Members

		public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => Dictionary.GetEnumerator();

		#endregion

		#region IEnumerable Members

		IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Dictionary).GetEnumerator();

		#endregion

		#region INotifyCollectionChanged Members

		public event NotifyCollectionChangedEventHandler CollectionChanged;

		#endregion

		#region INotifyPropertyChanged Members

		public event PropertyChangedEventHandler PropertyChanged;

		#endregion

		public void AddOrReplace(TKey key, TValue value)
		{
			if (ContainsKey(key))
			{
				Remove(key, suppressNotifications: true);
				Add(key, value);
			}
			else
			{
				Add(key, value);
			}
		}

		/// <summary>
		/// NotImplementedException
		/// </summary>
		/// <param name="items"></param>
		public void AddRange(IDictionary<TKey, TValue> items)
		{
			throw new NotImplementedException();
		}

		private void Insert(TKey key, TValue value, bool add)
		{
			lock(Lock)
			{
				if (key == null) throw new ArgumentNullException(nameof(key));

				TValue item;
				if (Dictionary.TryGetValue(key, out item))
				{
					if (add) throw new ArgumentException("An item with the same key has already been added.");
					if (Equals(item, value)) return;
					Dictionary[key] = value;

					OnCollectionChanged(NotifyCollectionChangedAction.Replace, new KeyValuePair<TKey, TValue>(key, value), new KeyValuePair<TKey, TValue>(key, item));
					OnPropertyChanged(key.ToString());
				}
				else
				{
					Dictionary[key] = value;

					OnCollectionChanged(NotifyCollectionChangedAction.Add, new KeyValuePair<TKey, TValue>(key, value));
					OnPropertyChanged(key.ToString());
				}
			}
		}

		private void OnPropertyChanged()
		{
			OnPropertyChanged(CountString);
			OnPropertyChanged(IndexerName);
			OnPropertyChanged(KeysName);
			OnPropertyChanged(ValuesName);
		}

		protected virtual void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		private void OnCollectionChanged()
		{
			OnPropertyChanged();
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}

		private void OnCollectionChanged(NotifyCollectionChangedAction action, KeyValuePair<TKey, TValue> changedItem)
		{
			OnPropertyChanged();
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(action, changedItem, 0));
		}

		private void OnCollectionChanged(NotifyCollectionChangedAction action, KeyValuePair<TKey, TValue> newItem, KeyValuePair<TKey, TValue> oldItem)
		{
			OnPropertyChanged();
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(action, newItem, oldItem, 0));
		}

		private void OnCollectionChanged(NotifyCollectionChangedAction action, IList newItems)
		{
			OnPropertyChanged();
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(action, newItems, 0));
		}
	}
}