using NBitcoin;
using Soju.WabiSabi.Backend.Rounds;
using WabiSabi.Crypto.Randomness;

namespace Soju.WabiSabi.Client.CoinJoin.Client.Decomposer;

public class OutputProvider
{
	// FIX: Should rework this so the scripts are chooseable and if so then delete this!!!
	public static readonly ScriptType[] DefaultSupportedScriptTypes = [ScriptType.P2WPKH, ScriptType.Taproot];
	
	private readonly WasabiRandom _random;
	public readonly ScriptType[] SupportedScriptTypes;

	public OutputProvider(WasabiRandom? random = null)
	{
		_random = random ?? SecureRandom.Instance;
		SupportedScriptTypes = DefaultSupportedScriptTypes;
	}

	public virtual IEnumerable<Output> GetOutputs(
		RoundParameters roundParameters,
		IEnumerable<Money> registeredCoinEffectiveValues,
		IEnumerable<Money> theirCoinEffectiveValues,
		int availableVsize)
	{
		AmountDecomposer amountDecomposer = new(
			roundParameters.MiningFeeRate,
			roundParameters.CalculateMinReasonableOutputAmount(SupportedScriptTypes),
			roundParameters.AllowedOutputAmounts.Max,
			availableVsize,
			SupportedScriptTypes,
			_random);

		return amountDecomposer.Decompose(registeredCoinEffectiveValues, theirCoinEffectiveValues);
	}
}
