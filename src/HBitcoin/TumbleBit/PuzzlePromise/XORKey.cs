using HBitcoin.TumbleBit.BouncyCastle.Math;
using System;

namespace HBitcoin.TumbleBit.PuzzlePromise
{
	public class XORKey
	{
		public XORKey(PuzzleSolution puzzleSolution) : this(puzzleSolution._Value)
		{

		}
		public XORKey(RsaPubKey pubKey) : this(Utils.GenerateEncryptableInteger(pubKey._Key))
		{
		}
		public XORKey(byte[] key)
		{
			if(key == null)
				throw new ArgumentNullException(nameof(key));
			if(key.Length != KeySize)
				throw new ArgumentException("Key has invalid length from expected " + KeySize);
			_Value = new BigInteger(1, key);
		}

		private XORKey(BigInteger value)
		{
			_Value = value ?? throw new ArgumentNullException(nameof(value));
		}

		private BigInteger _Value;

		public byte[] XOR(byte[] data)
		{
			var keyBytes = ToBytes();
			var keyHash = PromiseUtils.SHA512(keyBytes, 0, keyBytes.Length);
			var encrypted = new byte[data.Length];
			for(int i = 0; i < encrypted.Length; i++)
			{

				encrypted[i] = (byte)(data[i] ^ keyHash[i % keyHash.Length]);
			}
			return encrypted;
		}


		private const int KeySize = 256;
		public byte[] ToBytes()
		{
			var keyBytes = _Value.ToByteArrayUnsigned();
			Utils.Pad(ref keyBytes, KeySize);
			return keyBytes;
		}
	}
}
