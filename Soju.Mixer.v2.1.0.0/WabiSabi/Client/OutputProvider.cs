using NBitcoin;
using System.Linq;
using System.Collections.Generic;
using WabiSabi.Crypto.Randomness;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client.CoinJoin.Client.Decomposer;
using Soju.WabiSabi.Client.CoinJoin.Client;

namespace Soju.WabiSabi.Client;

public class OutputProvider
{
	public static readonly ScriptType[] DefaultSupportedScriptTypes = [ScriptType.P2WPKH, ScriptType.Taproot];
	
	public readonly ScriptType[] SupportedScriptTypes;
	private WasabiRandom Random { get; }

	public OutputProvider(ScriptType[]? supportedScriptTypes = null, WasabiRandom? random = null)
	{
		SupportedScriptTypes = supportedScriptTypes ?? DefaultSupportedScriptTypes;
		Random = random ?? SecureRandom.Instance;
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
			Random);

		return amountDecomposer.Decompose(registeredCoinEffectiveValues, theirCoinEffectiveValues).ToArray();
	}
}
