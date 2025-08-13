using NBitcoin;

namespace Soju.WabiSabi.Backend.Rounds;

public class RoundParameterFactory
{
    public RoundParameterFactory(WabiSabiConfig config)
    {
        Config = config;
    }

    public WabiSabiConfig Config { get; }

    public virtual RoundParameters CreateRoundParameter(FeeRate feeRate, Money maxSuggestedAmount) =>
        RoundParameters.Create(
            Config,
            feeRate,
            maxSuggestedAmount);
}