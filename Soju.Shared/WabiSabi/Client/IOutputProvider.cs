using NBitcoin;
using Soju.WabiSabi.Backend.Rounds;

namespace Soju.WabiSabi.Client;

public interface IOutputProvider
{
	public IEnumerable<TxOut> GetOutputs(
		uint256 roundId,
		RoundParameters roundParameters,
		IEnumerable<Money> registeredCoinEffectiveValues,
		IEnumerable<Money> theirCoinEffectiveValues,
		int availableVsize);
}