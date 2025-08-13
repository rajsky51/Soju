using System.Text.Json.Serialization;

namespace Soju.MyJson;

public record WalletConfig
{
    [JsonPropertyName("funds")]
    public required List<Int64> Funds { get; init; }
    [JsonPropertyName("anon_score_target")]
    public string? AnonScoreTarget { get; init; }
}