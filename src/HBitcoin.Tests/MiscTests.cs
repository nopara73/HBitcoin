using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HBitcoin.FullBlockSpv;
using HBitcoin.KeyManagement;
using NBitcoin;
using Xunit;

namespace HBitcoin.Tests
{
    public class MiscTests
    {
		[Fact]
		public void ObservableDictionaryTest()
		{
			ConcurrentObservableDictionary<int, string> foo = new ConcurrentObservableDictionary<int, string>();
			foo.Add(1, "foo");

			var times = 0;
			foo.CollectionChanged += delegate
			{
				times++;
			};

			foo.AddOrReplace(1, "boo");

			Assert.Equal(times, 1);
		}
	}
}
