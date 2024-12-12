using NBitcoin;
using Soju.Batching;

namespace Soju;

public interface IWallet
{
	string WalletName { get; }
	WalletId WalletId { get; }
	bool IsUnderPlebStop { get; }
	bool IsMixable { get; }

	OutputProvider OutputProvider { get; }

	int AnonScoreTarget { get; }
	bool ConsolidationMode { get; set; }
	bool RedCoinIsolation { get; }
	CoinjoinSkipFactors CoinjoinSkipFactors { get; }

	Money LiquidityClue { get; }

	bool IsWalletPrivate();
	IEnumerable<DumbCoin> GetCoinJoinCoinCandidates();
	int RemoveCoins(IEnumerable<DumbCoin> coinsToRemove);
	int AddCoins(IEnumerable<DumbCoin> coinsToAdd);
}
