using NBitcoin;
using Newtonsoft.Json;
using HBitcoin.TumbleBit.JsonConverters;
using Newtonsoft.Json.Converters;
using HBitcoin.TumbleBit.ClassicTumbler;
using System.Linq;

namespace HBitcoin.TumbleBit
{
	public class Serializer
	{
		public static void RegisterFrontConverters(JsonSerializerSettings settings, Network network = null, bool prettyPrint = false)
		{
			settings.Converters.Add(new RsaKeyJsonConverter());
			settings.Converters.Add(new SerializerBaseJsonConverter());
			settings.Converters.Add(new StringEnumConverter());
			NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(settings, network);

			if(prettyPrint)
			{
				var index = 0;
				var btcSerializable = settings.Converters.Where((o, i) =>
				{
					index = i;
					return o is NBitcoin.JsonConverters.BitcoinSerializableJsonConverter;
				}).First();

				var btcSerializableOverride = new OverridesJsonConverter(btcSerializable);

				btcSerializableOverride.MaskTypes.Add(typeof(ClassicTumblerParameters));
				btcSerializableOverride.MaskTypes.Add(typeof(OverlappedCycleGenerator));
				btcSerializableOverride.MaskTypes.Add(typeof(CycleParameters));

				settings.Converters[index] = btcSerializableOverride;
			}
		}

		public static T ToObject<T>(string data) => ToObject<T>(data, null);

		public static T ToObject<T>(string data, Network network)
		{
			var settings = new JsonSerializerSettings
			{
				Formatting = Formatting.Indented
			};
			RegisterFrontConverters(settings, network);
			return JsonConvert.DeserializeObject<T>(data, settings);
		}

		public static string ToString<T>(T response, Network network, bool prettyPrint = false)
		{
			var settings = new JsonSerializerSettings
			{
				Formatting = Formatting.Indented
			};
			RegisterFrontConverters(settings, network, prettyPrint);
			return JsonConvert.SerializeObject(response, settings);
		}
		public static string ToString<T>(T response) => ToString<T>(response, null);

		public static T Clone<T>(T data)
		{
			var o = ToString(data);
			return ToObject<T>(o);
		}
	}
}
