using NBitcoin;
using HBitcoin.TumbleBit.BouncyCastle.Crypto.Engines;
using HBitcoin.TumbleBit.BouncyCastle.Crypto.Parameters;
using HBitcoin.TumbleBit.BouncyCastle.Math;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HBitcoin.TumbleBit
{
	public static class Utils
	{
		public static IEnumerable<T> TopologicalSort<T>(this IEnumerable<T> nodes,
												Func<T, IEnumerable<T>> dependsOn)
		{
			var result = new List<T>();
			var elems = nodes.ToDictionary(node => node,
										   node => new HashSet<T>(dependsOn(node)));
			while(elems.Count > 0)
			{
				var elem = elems.FirstOrDefault(x => x.Value.Count == 0);
				if(elem.Key == null)
				{
					//cycle detected can't order
					return nodes;
				}
				elems.Remove(elem.Key);
				foreach(var selem in elems)
				{
					selem.Value.Remove(elem.Key);
				}
				result.Add(elem.Key);
			}
			return result;
		}
		internal static byte[] ChachaEncrypt(byte[] data, ref byte[] key)
		{
			byte[] iv = null;
			return ChachaEncrypt(data, ref key, ref iv);
		}

		public static byte[] Combine(params byte[][] arrays)
		{
			var len = arrays.Select(a => a.Length).Sum();
			var offset = 0;
			var combined = new byte[len];
			foreach(var array in arrays)
			{
				Array.Copy(array, 0, combined, offset, array.Length);
				offset += array.Length;
			}
			return combined;
		}
		internal static byte[] ChachaEncrypt(byte[] data, ref byte[] key, ref byte[] iv)
		{
			var engine = new ChaChaEngine();
			key = key ?? RandomUtils.GetBytes(ChachaKeySize);
			iv = iv ?? RandomUtils.GetBytes(ChachaKeySize / 2);
			engine.Init(true, new ParametersWithIV(new KeyParameter(key), iv));
			var result = new byte[iv.Length + data.Length];
			Array.Copy(iv, result, iv.Length);
			engine.ProcessBytes(data, 0, data.Length, result, iv.Length);
			return result;
		}

		internal const int ChachaKeySize = 128 / 8;
		internal static byte[] ChachaDecrypt(byte[] encrypted, byte[] key)
		{
			var engine = new ChaChaEngine();
			var iv = new byte[ChachaKeySize / 2];
			Array.Copy(encrypted, iv, iv.Length);
			engine.Init(false, new ParametersWithIV(new KeyParameter(key), iv));
			var result = new byte[encrypted.Length - iv.Length];
			engine.ProcessBytes(encrypted, iv.Length, encrypted.Length - iv.Length, result, 0);
			return result;
		}

		internal static void Pad(ref byte[] bytes, int keySize)
		{
			var paddSize = keySize - bytes.Length;
			if (bytes.Length == keySize)
				return;
			if(paddSize < 0)
				throw new InvalidOperationException("Bug in NTumbleBit, copy the stacktrace and send us");
			var padded = new byte[paddSize + bytes.Length];
			Array.Copy(bytes, 0, padded, paddSize, bytes.Length);
			bytes = padded;
		}

		internal static BigInteger GenerateEncryptableInteger(RsaKeyParameters key)
		{
			while(true)
			{
				var bytes = RandomUtils.GetBytes(RsaKey.KeySize / 8);
				var input = new BigInteger(1, bytes);
				if (input.CompareTo(key.Modulus) >= 0)
					continue;
				return input;
			}
		}

		// http://stackoverflow.com/a/14933880/2061103
		public static async Task DeleteRecursivelyWithMagicDustAsync(string destinationDir, CancellationToken ctsToken = default(CancellationToken))
		{
			const int magicDust = 10;
			for (var gnomes = 1; gnomes <= magicDust; gnomes++)
			{
				try
				{
					Directory.Delete(destinationDir, true);
				}
				catch (DirectoryNotFoundException)
				{
					return;  // good!
				}
				catch (IOException)
				{
					if (gnomes == magicDust)
						throw;
					// System.IO.IOException: The directory is not empty
					Debug.WriteLine("Gnomes prevent deletion of {0}! Applying magic dust, attempt #{1}.", destinationDir, gnomes);

					// see http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true for more magic
					await Task.Delay(100, ctsToken).ConfigureAwait(false);
					continue;
				}
				catch (UnauthorizedAccessException)
				{
					if (gnomes == magicDust)
						throw;
					// Wait, maybe another software make us authorized a little later
					Debug.WriteLine("Gnomes prevent deletion of {0}! Applying magic dust, attempt #{1}.", destinationDir, gnomes);

					// see http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true for more magic
					await Task.Delay(100, ctsToken).ConfigureAwait(false);
					continue;
				}
				return;
			}
			// depending on your use case, consider throwing an exception here
		}
	}
}
