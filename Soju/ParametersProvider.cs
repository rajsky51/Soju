using System.Collections.Immutable;
using NBitcoin;
using Soju.Json;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Models;

namespace Soju;

public static class ParametersProvider
{
	public readonly static RoundParameters DefaultRoundParams = new RoundParameters(
		miningFeeRate        : new(Money.Satoshis(20_000)),
		maxSuggestedAmount   : Money.Coins(43_000),
		minInputCountByRound : 10,
		maxInputCountByRound : 500,
		allowedInputAmounts  : new(Money.Satoshis(10_000), Money.Coins(43_000)),
		allowedOutputAmounts : new(Money.Satoshis(10_000), Money.Coins(43_000)),
		allowedInputTypes    : [ScriptType.Taproot, ScriptType.P2WPKH],
		allowedOutputTypes   : [ScriptType.Taproot, ScriptType.P2WPKH]
	);

	public static RoundParameters GetRoundParameters(WabiSabiConfig wsc)
	{
		decimal.TryParse(wsc.MinRegistrableAmount, out decimal minRegistrableAmount);
		decimal.TryParse(wsc.MaxRegistrableAmount, out decimal maxRegistrableAmount);

		FeeRate miningFeeRate = DefaultRoundParams.MiningFeeRate;
		Money maxSuggestedAmount = Money.Coins(maxRegistrableAmount);
		int maxInputCountByRound = wsc.MaxInputCountByRound;
		int minInputCountByRound = (int)(wsc.MaxInputCountByRound * wsc.MinInputCountByRoundMultiplier);
		MoneyRange allowedInputAmounts = new(Money.Coins(minRegistrableAmount), Money.Coins(maxRegistrableAmount));
		MoneyRange allowedOutputAmounts = allowedInputAmounts;

		List<ScriptType> allowedInputTypes = [];
		if (wsc.AllowP2wpkhInputs) allowedInputTypes.Add(ScriptType.P2WPKH);
		if (wsc.AllowP2trInputs) allowedInputTypes.Add(ScriptType.Taproot);

		List<ScriptType> allowedOutputTypes = [];
		if (wsc.AllowP2wpkhOutputs) allowedOutputTypes.Add(ScriptType.P2WPKH);
		if (wsc.AllowP2wpkhOutputs) allowedOutputTypes.Add(ScriptType.Taproot);

		RoundParameters roundParams = new(
			miningFeeRate        : miningFeeRate,
			maxSuggestedAmount   : maxSuggestedAmount,
			minInputCountByRound : minInputCountByRound,
			maxInputCountByRound : maxInputCountByRound,
			allowedInputAmounts  : allowedInputAmounts,
			allowedOutputAmounts : allowedOutputAmounts,
			allowedInputTypes    : allowedInputTypes.ToImmutableSortedSet(),
			allowedOutputTypes   : allowedOutputTypes.ToImmutableSortedSet()
		);

		return roundParams;
	}
}