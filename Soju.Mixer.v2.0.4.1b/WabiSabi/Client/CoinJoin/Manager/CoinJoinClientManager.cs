using System.Collections.Immutable;
using System.Diagnostics;
using NBitcoin;
using Soju.Blockchain.Keys;
using Soju.Wallets;
using WabiSabi.Crypto.Randomness;

namespace Soju.WabiSabi.Client;

public class CoinJoinClientManager
{
	public Wallet Wallet;
	public string CoordinatorIdentifier;
	public LiquidityClueProvider LiquidityClueProvider;
	
	public CoinJoinClientManager(
		Wallet wallet,
		string coordinatorIdentifier)
	{
		Wallet = wallet;
		CoordinatorIdentifier = coordinatorIdentifier;
		LiquidityClueProvider = new LiquidityClueProvider();
	}
	
	public CoinJoinClientContext CreateCoinJoinClientCtx()
	{
		OutputProvider outputProvider = new(Wallet.DestinationProvider, InsecureRandom.Instance);
		CoinJoinClient cjClient = new CoinJoinClient(
			Wallet.KeyChain,
			outputProvider,
			CoordinatorIdentifier,
			CoinJoinCoinSelector.FromWallet(Wallet),
			LiquidityClueProvider,
			Wallet.FeeRateMedianTimeFrame);
		
		return new CoinJoinClientContext(Wallet, cjClient);
	}
}