using NBitcoin;

namespace Soju.WabiSabi.Backend.Rounds.CoinJoinStorage;

public interface ICoinJoinIdStore
{
	bool TryAdd(uint256 id);

	bool Contains(uint256 id);
}
