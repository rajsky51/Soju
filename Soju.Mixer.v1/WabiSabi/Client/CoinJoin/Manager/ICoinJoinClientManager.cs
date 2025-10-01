using Soju.Wallets;

namespace Soju.WabiSabi.Client;

public interface ICoinJoinClientManager
{
	CoinJoinClientContext CreateCoinJoinClientCtx();
	WalletId GetWalletId();
}
