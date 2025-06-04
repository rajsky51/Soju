using NBitcoin;
using WabiSabi.Crypto.Randomness;
using Soju.WabiSabi.Models;
using Soju.WabiSabi.Client;


namespace Soju.WabiSabi.Backend.Rounds;

public static class RoundParametersExtensions
{
    /// <returns>Min output amount that's economically reasonable to be registered with current network conditions.</returns>
    /// <remarks>It won't be smaller than min allowed output amount.</remarks>
    public static Money CalculateMinReasonableOutputAmount(this RoundParameters roundParameters)
    {
        var minEconomicalOutput = roundParameters.MiningFeeRate.GetFee(roundParameters.MaxVsizeInputOutputPair);
        return Math.Max(minEconomicalOutput, roundParameters.AllowedOutputAmounts.Min);
    }
	
    public static Money CalculateSmallestReasonableEffectiveDenomination(this RoundParameters roundParameters, WasabiRandom? random = null)
    {
        random ??= SecureRandom.Instance;
        return CalculateSmallestReasonableEffectiveDenomination(CalculateMinReasonableOutputAmount(roundParameters), roundParameters.AllowedOutputAmounts.Max, roundParameters.MiningFeeRate, roundParameters.MaxVsizeInputOutputPairScriptType, random);
    }

    /// <returns>Smallest effective denom that's larger than min reasonable output amount. </returns>
    public static Money CalculateSmallestReasonableEffectiveDenomination(
        Money minReasonableOutputAmount,
        Money maxAllowedOutputAmount,
        FeeRate feeRate,
        ScriptType maxVsizeInputOutputPairScriptType,
        WasabiRandom random)
    {
        var smallestEffectiveDenom = DenominationBuilder.CreateDenominations(
                minReasonableOutputAmount,
                maxAllowedOutputAmount,
                feeRate,
                new List<ScriptType>() { maxVsizeInputOutputPairScriptType },
                random)
            .Min(x => x.EffectiveCost);

        return smallestEffectiveDenom is null
            ? throw new InvalidOperationException("Something's wrong with the denomination creation or with the parameters it got.")
            : smallestEffectiveDenom;
    }
	
    /// <returns>Min: must be larger than the smallest economical denom. Max: max allowed in the round.</returns>
    public static MoneyRange CalculateReasonableOutputAmountRange(this RoundParameters roundParameters, WasabiRandom random) => new(CalculateSmallestReasonableEffectiveDenomination(roundParameters, random), roundParameters.AllowedOutputAmounts.Max);
}