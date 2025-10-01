using System.Collections.Immutable;
using NBitcoin;
using Soju.Blockchain.TransactionOutputs;
using Soju.Wallets;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Client.CredentialDependencies;
using Soju.WabiSabi.Models;

namespace Soju.WabiSabi.Client;

public class CoinJoinClientContext
{
	public Wallet Wallet;
	public CoinJoinConfiguration CoinJoinConfiguration;
	public CoinJoinClient CoinJoinClient;
	public WalletId WalletId; // NOTE: Also a unique id for the manager
	public ImmutableArray<AliceClient> RegisteredAliceClients;
	public ImmutableArray<TxOut> WantedOutputs;
	public DependencyGraph Graph;
	
	public CoinJoinClientContext(
		Wallet wallet,
		CoinJoinConfiguration cjConfig)
	{
		Wallet = wallet;
		WalletId = wallet.WalletId;
		CoinJoinConfiguration = cjConfig;
		CoinJoinClient = null;
	}
	
	public List<SmartCoin> StartRoundAndGetCoins(RoundState roundState)
	{
		CoinJoinClient = new CoinJoinClient();
		
		IEnumerable<SmartCoin> coins = CoinJoinClient.StartCoinJoin(roundState, Wallet.GetCoinjoinCoinCandidates);
		return coins.ToList();
	}
}

public record CoinJoinConfiguration(string CoordinatorIdentifier,  decimal MaxCoinJoinMiningFeeRate, int AbsoluteMinInputCount, bool AllowSoloCoinjoining);
