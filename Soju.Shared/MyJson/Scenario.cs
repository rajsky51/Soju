using System.Text.Json.Serialization;

namespace Soju.MyJson;

public record CoinjoinScenario
{
    [JsonPropertyName("name")]
    public string Name { get; init; }
    [JsonPropertyName("rounds")]
    public required int Rounds { get; init; }
    [JsonPropertyName("default_anon_score_target")]
    public required float DefaultAnonScoreTarget { get; init; }
    [JsonPropertyName("backend")]
    public WabiSabiConfig? Backend { get; init; }
    [JsonPropertyName("wallets")]
    public required List<WalletConfig> Wallets { get; init; }
}