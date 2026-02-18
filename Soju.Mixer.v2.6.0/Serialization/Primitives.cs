using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Soju.Helpers;
using JsonException = System.Text.Json.JsonException;

namespace Soju.Serialization;

public delegate JsonNode Encoder<in T>(T value);
public delegate Result<T, string> Decoder<T>(JsonElement value);

public static class JsonEncoder
{
	private static readonly JsonSerializerOptions Indented = new()
	{
		WriteIndented = true,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public static string ToString<T>(T obj, Encoder<T> encoder) =>
		encoder(obj).ToJsonString();

	public static string ToReadableString<T>(T obj, Encoder<T> encoder) =>
		encoder(obj).ToJsonString(Indented);
}

public static class JsonDecoder
{
	public static Func<string, Result<T, string>> FromString<T>(Decoder<T> decoder) =>
		value =>
		{
			try
			{
				var jsonDocument = JsonDocument.Parse(value);
				return decoder(jsonDocument.RootElement);
			}
			catch (JsonException e)
			{
				return Result<T, string>.Fail(e.Message);
			}
		};

	internal static T? FromString<T>(string json, Decoder<T> decoder) =>
		FromString(decoder)(json).AsNullable();

	public static Func<Stream, Task<Result<T, string>>> FromStreamAsync<T>(Decoder<T> decoder) =>
		async value =>
		{
			try
			{
				var jsonDocument = await JsonDocument.ParseAsync(value).ConfigureAwait(false);
				return decoder(jsonDocument.RootElement);
			}
			catch (JsonException e)
			{
				return Result<T, string>.Fail(e.Message);
			}
		};

	public static Func<Stream, Result<T, string>> FromStream<T>(Decoder<T> decoder) =>
		value =>
		{
			try
			{
				var jsonDocument = JsonDocument.Parse(value);
				return decoder(jsonDocument.RootElement);
			}
			catch (JsonException e)
			{
				return Result<T, string>.Fail(e.Message);
			}
		};
}
