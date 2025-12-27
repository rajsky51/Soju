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
		CoinJoinClient cjClient = new CoinJoinClient(
			Wallet.KeyChain,
			Wallet.OutputProvider,
			CoinJoinCoinSelector.FromWallet(Wallet),
			CoinJoinConfiguration,
			LiquidityClueProvider,
			Wallet.FeeRateMedianTimeFrame,
			Wallet.CoinjoinSkipFactors);
		
		return new CoinJoinClientContext(Wallet, cjClient);
	}
}