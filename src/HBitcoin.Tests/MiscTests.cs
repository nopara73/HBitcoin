using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HBitcoin.Tests
{
    public class MiscTests
    {
		[Fact]
		public void ObservableDictionaryTest()
		{
			ObservableDictionary<int, string> foo = new ObservableDictionary<int, string>();
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
