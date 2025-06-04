using NBitcoin;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client.CoinJoin.Client.Decomposer;
using WabiSabi.Crypto.Randomness;

namespace Soju.WabiSabi.Client;

public class OutputProvider
{
	public static readonly ScriptType[] DefaultSupportedScriptTypes = [ScriptType.P2WPKH, ScriptType.Taproot];
	
	public readonly ScriptType[] SupportedScriptTypes;
	private WasabiRandom Random { get; }
	
	public OutputProvider(ScriptType[]? supportedScriptTypes = null, WasabiRandom? random = null)
	{
		SupportedScriptTypes = supportedScriptTypes;
		Random = random ?? SecureRandom.Instance;
	}

	public virtual IEnumerable<Output> GetOutputs(
		RoundParameters roundParameters,
		IEnumerable<Money> registeredCoinEffectiveValues,
		IEnumerable<Money> theirCoinEffectiveValues,
		int availableVsize)
	{
		AmountDecomposer amountDecomposer = new(roundParameters.MiningFeeRate, roundParameters.CalculateMinReasonableOutputAmount(), roundParameters.AllowedOutputAmounts.Max, availableVsize, roundParameters.AllowedOutputTypes, Random);

		return amountDecomposer.Decompose(registeredCoinEffectiveValues, theirCoinEffectiveValues).ToArray();
	}
}
