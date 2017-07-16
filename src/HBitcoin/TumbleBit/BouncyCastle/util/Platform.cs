using System;
using System.Globalization;

#if SILVERLIGHT || PORTABLE
using System.Collections.Generic;
#else
using System.Collections;
#endif

namespace NTumbleBit.BouncyCastle.Utilities
{
	internal abstract class Platform
	{
		private static readonly CompareInfo InvariantCompareInfo = CultureInfo.InvariantCulture.CompareInfo;

#if NETCF_1_0 || NETCF_2_0
        private static string GetNewLine()
        {
            MemoryStream buf = new MemoryStream();
            StreamWriter w = new StreamWriter(buf, Encoding.UTF8);
            w.WriteLine();
            Dispose(w);
            byte[] bs = buf.ToArray();
            return Encoding.UTF8.GetString(bs, 0, bs.Length);
        }
#else
		private static string GetNewLine() => Environment.NewLine;
#endif

		internal static bool EqualsIgnoreCase(string a, string b) => ToUpperInvariant(a) == ToUpperInvariant(b);

#if NETCF_1_0 || NETCF_2_0 || SILVERLIGHT || PORTABLE
		internal static string GetEnvironmentVariable(
			string variable)
		{
			return null;
		}
#else
		internal static string GetEnvironmentVariable(
            string variable)
        {
            try
            {
                return Environment.GetEnvironmentVariable(variable);
            }
            catch (System.Security.SecurityException)
            {
                // We don't have the required permission to read this environment variable,
                // which is fine, just act as if it's not set
                return null;
            }
        }
#endif

#if NETCF_1_0
        internal static Exception CreateNotImplementedException(
            string message)
        {
            return new Exception("Not implemented: " + message);
        }

        internal static bool Equals(
            object	a,
            object	b)
        {
            return a == b || (a != null && b != null && a.Equals(b));
        }
#else
		internal static Exception CreateNotImplementedException(
			string message) => new NotImplementedException(message);
#endif

#if SILVERLIGHT || PORTABLE
		internal static System.Collections.IList CreateArrayList()
		{
			return new List<object>();
		}
		internal static System.Collections.IList CreateArrayList(int capacity)
		{
			return new List<object>(capacity);
		}
		internal static System.Collections.IList CreateArrayList(System.Collections.ICollection collection)
		{
			System.Collections.IList result = new List<object>(collection.Count);
			foreach(object o in collection)
			{
				result.Add(o);
			}
			return result;
		}
		internal static System.Collections.IList CreateArrayList(System.Collections.IEnumerable collection)
		{
			System.Collections.IList result = new List<object>();
			foreach(object o in collection)
			{
				result.Add(o);
			}
			return result;
		}
		internal static System.Collections.IDictionary CreateHashtable()
		{
			return new Dictionary<object, object>();
		}
		internal static System.Collections.IDictionary CreateHashtable(int capacity)
		{
			return new Dictionary<object, object>(capacity);
		}
		internal static System.Collections.IDictionary CreateHashtable(System.Collections.IDictionary dictionary)
		{
			System.Collections.IDictionary result = new Dictionary<object, object>(dictionary.Count);
			foreach(System.Collections.DictionaryEntry entry in dictionary)
			{
				result.Add(entry.Key, entry.Value);
			}
			return result;
		}
#else
		internal static IList CreateArrayList()
        {
            return new ArrayList();
        }
		internal static IList CreateArrayList(int capacity) => new ArrayList(capacity);

		internal static IList CreateArrayList(ICollection collection)
        {
            return new ArrayList(collection);
        }
        internal static IList CreateArrayList(IEnumerable collection)
        {
            var result = new ArrayList();
			foreach (object o in collection)
            {
                result.Add(o);
            }
            return result;
        }
		internal static IDictionary CreateHashtable() => new Hashtable();

		internal static IDictionary CreateHashtable(int capacity)
        {
            return new Hashtable(capacity);
        }
		internal static IDictionary CreateHashtable(IDictionary dictionary) => new Hashtable(dictionary);
#endif

		internal static string ToLowerInvariant(string s) => s.ToLowerInvariant();

		internal static string ToUpperInvariant(string s) => s.ToUpperInvariant();

		internal static readonly string NewLine = GetNewLine();
		internal static void Dispose(IDisposable d)
        {
            d.Dispose();
        }
		internal static int IndexOf(string source, string value) => InvariantCompareInfo.IndexOf(source, value, CompareOptions.Ordinal);

		internal static int LastIndexOf(string source, string value) => InvariantCompareInfo.LastIndexOf(source, value, CompareOptions.Ordinal);

		internal static bool StartsWith(string source, string prefix) => InvariantCompareInfo.IsPrefix(source, prefix, CompareOptions.Ordinal);

		internal static bool EndsWith(string source, string suffix) => InvariantCompareInfo.IsSuffix(source, suffix, CompareOptions.Ordinal);

		internal static string GetTypeName(object obj) => obj.GetType().FullName;
	}
}
