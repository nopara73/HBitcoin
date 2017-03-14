using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HBitcoin.FullBlockSpv
{
    public class TransactionHeight
    {
	    public TransactionHeightType Type { get; }
	    private readonly int _value;

	    public int Value
	    {
		    get
		    {
				if(Type == TransactionHeightType.Chain)
					return _value;
			    if(Type == TransactionHeightType.MemPool)
				    return int.MaxValue - 1;
			    //if(Type == TransactionHeightType.NotPropagated)
				return int.MaxValue;
		    }
	    }

	    public TransactionHeight(int height)
	    {
		    if(height < 0) throw new ArgumentException($"{nameof(height)} : {height} cannot be < 0");
			if (height == int.MaxValue) Type = TransactionHeightType.NotPropagated;
			else if (height == int.MaxValue - 1) Type = TransactionHeightType.MemPool;
			else Type = TransactionHeightType.Chain;
			_value = height;
	    }
		public TransactionHeight(TransactionHeightType type)
		{
			if(type == TransactionHeightType.Chain) throw new NotSupportedException($"For {type} height must be specified");
			Type = type;
			_value = Value;
		}

		public override string ToString()
		{
			if(Type == TransactionHeightType.Chain) return Value.ToString();
			else return Type.ToString();
		}
	}
	public enum TransactionHeightType
	{
		Chain,
		MemPool,
		NotPropagated
	}
}
