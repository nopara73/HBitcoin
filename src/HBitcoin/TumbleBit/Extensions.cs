using NBitcoin;
using System;
using System.Text;
using System.Threading.Tasks;

namespace HBitcoin.TumbleBit
{
	public static class Extensions
	{
		public static void ReadWriteC(this BitcoinStream bs, ref Network network)
		{
			if(bs.Serializing)
			{
				var str = network.ToString();
				bs.ReadWriteC(ref str);
			}
			else
			{
				var str = string.Empty;
				bs.ReadWriteC(ref str);
				network = Network.GetNetwork(str);
			}
		}
		public static void ReadWriteC(this BitcoinStream bs, ref string str)
		{
			if(bs.Serializing)
			{
				var bytes = Encoding.ASCII.GetBytes(str);
				bs.ReadWriteAsVarString(ref bytes);
			}
			else
			{
				byte[] bytes = null;
				bs.ReadWriteAsVarString(ref bytes);
				str = Encoding.ASCII.GetString(bytes);
			}
		}

		public static void ReadWriteC(this BitcoinStream bs, ref RsaPubKey pubKey)
		{
			if(bs.Serializing)
			{
				var bytes = pubKey == null ? new byte[0] : pubKey.ToBytes();
				bs.ReadWriteAsVarString(ref bytes);
			}
			else
			{
				byte[] bytes = null;
				bs.ReadWriteAsVarString(ref bytes);
				pubKey = bytes.Length == 0 ? null : new RsaPubKey(bytes);
			}
		}

		public static void ReadWriteC(this BitcoinStream bs, ref Money money)
		{
			if(bs.Serializing)
			{
				var satoshis = checked((ulong)money.Satoshi);
				bs.ReadWrite(ref satoshis);
			}
			else
			{
				var satoshis = 0UL;
				bs.ReadWrite(ref satoshis);
				money = Money.Satoshis(satoshis);
			}
		}

		public static ScriptCoin Clone(this ScriptCoin scriptCoin) => new ScriptCoin(scriptCoin, scriptCoin.Redeem);

		public static async Task<TResult> WithTimeout<TResult>(this Task<TResult> task, TimeSpan timeout)
		{
			if (task == await Task.WhenAny(task, Task.Delay(timeout)))
			{
				return await task;
			}
			throw new TimeoutException();
		}
	}
}
