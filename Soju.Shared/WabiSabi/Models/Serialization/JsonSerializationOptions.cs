using Newtonsoft.Json;
// using Soju.JsonConverters;
// using Soju.JsonConverters.Bitcoin;
// using Soju.JsonConverters.Collections;
// using Soju.JsonConverters.Timing;
// using Soju.WabiSabi.Crypto.Serialization;

namespace Soju.WabiSabi.Models.Serialization;

public class JsonSerializationOptions
{
    private static readonly JsonSerializerSettings CurrentSettings = new()
    {
        Converters = new List<JsonConverter>()
        {
        }
    };

    public static readonly JsonSerializationOptions Default = new();

    private JsonSerializationOptions()
    {
    }

    public JsonSerializerSettings Settings => CurrentSettings;
}