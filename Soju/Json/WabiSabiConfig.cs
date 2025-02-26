namespace Soju.Json;

public record WabiSabiConfig
{
    public string MinRegistrableAmount { get; init; }
    public string MaxRegistrableAmount { get; init; }
    public int MaxInputCountByRound { get; init; }
    public decimal MinInputCountByRoundMultiplier { get; init; }
    public bool AllowP2wpkhInputs { get; init; }
    public bool AllowP2trInputs { get; init; }
    public bool AllowP2wpkhOutputs { get; init; }
    public bool AllowP2trOutputs { get; init; }
}