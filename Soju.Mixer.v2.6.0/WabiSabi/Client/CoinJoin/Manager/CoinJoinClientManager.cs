using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.Wallets;

namespace Soju.WabiSabi.Client;

public class CoinJoinClientManager
{
	public Wallet Wallet;
	public CoinJoinConfiguration CoinJoinConfiguration;
	public LiquidityClueProvider LiquidityClueProvider;
	
	public CoinJoinClientManager(
		Wallet wallet,
		CoinJoinConfiguration cjConfig)
	{
		Wallet = wallet;
		CoinJoinConfiguration = cjConfig;
		LiquidityClueProvider = new LiquidityClueProvider();
	}
	
	public CoinJoinClientContext CreateCoinJoinClientCtx()
	{
		// TODO: Add this to other versions
		if (Wallet.KeyChain is null)
		{
			throw new NotSupportedException("Wallet has no key chain.");
		}
		
		CoinJoinClient cjClient = new CoinJoinClient(
			Wallet.KeyChain,
			Wallet.OutputProvider,
			CoinJoinCoinSelector.FromWallet(Wallet),
			CoinJoinConfiguration,
			LiquidityClueProvider);
		
		return new CoinJoinClientContext(Wallet, cjClient);
	}
}
