using NBitcoin;
using WabiSabi.Crypto.Randomness;

namespace Soju.Decomposer;

public class OutputProvider
{
	private readonly WasabiRandom _random;
	public readonly ScriptType[] supportedScriptTypes;

	public OutputProvider(WasabiRandom? random = null)
	{
		_random = random ?? SecureRandom.Instance;
		// NOTE: Should rework this so the scripts are chooseable
		supportedScriptTypes = [ScriptType.P2WPKH, ScriptType.Taproot];
	}

	public virtual IEnumerable<Output> GetOutputs(
		RoundParameters roundParameters,
		IEnumerable<Money> registeredCoinEffectiveValues,
		IEnumerable<Money> theirCoinEffectiveValues,
		int availableVsize)
	{
		AmountDecomposer amountDecomposer = new(
			roundParameters.MiningFeeRate,
			roundParameters.CalculateMinReasonableOutputAmount(supportedScriptTypes),
			roundParameters.AllowedOutputAmounts.Max,
			availableVsize,
			supportedScriptTypes,
			_random);

		return amountDecomposer.Decompose(registeredCoinEffectiveValues, theirCoinEffectiveValues);
	}
}
