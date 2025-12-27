using System.Collections.Immutable;
using NBitcoin;
using Soju.Blockchain.TransactionOutputs;
using Soju.Helpers;
using Soju.Wallets;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Client.StatusChangedEvents;
using Soju.WabiSabi.Models;

namespace Soju.WabiSabi.Client;

public class CoinJoinClientContext
{
	public CoinJoinClient CoinJoinClient;
	public Wallet Wallet;
	public WalletId WalletId; // NOTE: Also a unique id for the manager
	
	public ImmutableArray<AliceClient> RegisteredAliceClients;
	public ImmutableArray<TxOut> WantedOutputs;
	
	public CoinJoinClientContext(
		Wallet wallet,
		CoinJoinClient cjClient)
	{
		CoinJoinClient = cjClient;
		Wallet = wallet;
		WalletId = wallet.WalletId;
	}
	
	public SmartCoin[] StartRoundAndGetCoins(RoundState roundState)
	{
		SmartCoin[] coinCandidates = new CoinsView(Wallet.GetCoinjoinCoinCandidates())
			.Available()
			.ToArray();
		
		if (!Wallet.BatchedPayments.AreTherePendingPayments) 
		{
			if (Wallet.IsWalletPrivate())
			{
				Wallet.LogTrace("All mixed!");
				throw new CoinJoinClientException(CoinjoinError.AllCoinsPrivate);
			}
			if (coinCandidates.All(x => x.IsPrivate(Wallet.AnonScoreTarget)))
			{
				throw new CoinJoinClientException(
					CoinjoinError.NoCoinsEligibleToMix,
					$"All coin candidates are already private");
			}
		}
		
		IEnumerable<SmartCoin> coins = CoinJoinClient.StartCoinJoin(roundState, coinCandidates);
		
		return coins.ToArray();
	}
}

public record CoinJoinConfiguration(string CoordinatorIdentifier, decimal MaxCoordinationFeeRate, decimal MaxCoinJoinMiningFeeRate, int AbsoluteMinInputCount, bool AllowSoloCoinjoining);
