using NBitcoin;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Client.CoinJoin.Client.Decomposer;
using WabiSabi.Crypto.Randomness;

namespace Soju.WabiSabi.Client;

public class OutputProvider
{
	public static readonly ScriptType[] DefaultSupportedScriptTypes = [ScriptType.P2WPKH, ScriptType.Taproot];
	
	public readonly ScriptType[] SupportedScriptTypes;
	private readonly WasabiRandom _random;

	public OutputProvider(ScriptType[]? supportedScriptTypes = null, WasabiRandom? random = null)
	{
		_random = random ?? SecureRandom.Instance;
		SupportedScriptTypes = supportedScriptTypes ?? DefaultSupportedScriptTypes;
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
