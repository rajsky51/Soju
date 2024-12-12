using NBitcoin;
using System.Linq;
using System.Collections.Generic;
using WabiSabi.Crypto.Randomness;
// using WalletWasabi.WabiSabi.Backend.Rounds;
// using WalletWasabi.WabiSabi.Client.CoinJoin.Client.Decomposer;

namespace Soju;

public class OutputProvider
{
	private readonly WasabiRandom Rng;
	private readonly IEnumerable<ScriptType> supportedScriptTypes = [ScriptType.P2WPKH, ScriptType.Taproot];

	public OutputProvider(WasabiRandom? random = null)
	{
		Rng = random ?? SecureRandom.Instance;
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
			Rng);

		return amountDecomposer.Decompose(registeredCoinEffectiveValues, theirCoinEffectiveValues);
	}
}
