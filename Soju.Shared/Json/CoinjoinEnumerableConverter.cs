using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Soju.WabiSabi.Client.CoinJoin;

namespace Soju.Json;

public class CoinjoinEnumerableConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType) return false;

        bool isEnumerable = Array.Exists(typeToConvert.GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        if (!isEnumerable) return false;
        if (typeToConvert.GetGenericArguments()[0] != typeof(CoinjoinResult)) return false;
        
        return true;
    }

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        Type elementType = typeToConvert.GetGenericArguments()[0];
        JsonConverter converter = (JsonConverter)Activator.CreateInstance(
            typeof(CoinjoinEnumerableConverterInner<>).MakeGenericType(elementType))!;

        return converter;
    }

    private class CoinjoinEnumerableConverterInner<T> : JsonConverter<IEnumerable<T>> where T : CoinjoinResult
    {
        public override IEnumerable<T>? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            throw new NotImplementedException(
                "JSON deserialization for IEnumerable<CoinjoinResult> is not implemented.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            IEnumerable<T> results,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteStartObject("coinjoins");
            foreach (CoinjoinResult result in results)
            {
                JsonSerializer.Serialize(writer, result, options);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }
}