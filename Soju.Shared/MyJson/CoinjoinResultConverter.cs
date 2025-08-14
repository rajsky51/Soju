using System.Text.Json;
using System.Text.Json.Serialization;
using NBitcoin.JsonConverters;
using Soju.WabiSabi.Client.CoinJoin;

namespace Soju.MyJson;

// TODO: Rewrite this after creating our own conjoin result
public class CoinjoinResultConverter : JsonConverter<CoinjoinResult>
{   
	public override CoinjoinResult Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		throw new NotImplementedException("JSON deserialization for CoinjoinResult is not implemented.");
	}

	public override void Write(
		Utf8JsonWriter writer,
		CoinjoinResult result,
		JsonSerializerOptions options)
	{
		string encodedRoundId = result.RoundId.ToString();
		
		writer.WriteStartObject(encodedRoundId);
		JsonSerializer.Serialize(writer, result.Transaction, options);
		writer.WriteString("round_id", encodedRoundId);
		writer.WriteNumber("mining_fee", result.MiningFee.Satoshi);
		writer.WriteNumber("coordination_fee", result.CoordinationFee.Satoshi);
		writer.WriteEndObject();
	}
}