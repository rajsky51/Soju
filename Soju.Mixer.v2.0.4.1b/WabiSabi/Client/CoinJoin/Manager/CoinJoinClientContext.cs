using NBitcoin;
using System.Collections.Immutable;
using Soju.Blockchain.TransactionOutputs;
using Soju.Exceptions;
using Soju.Helpers;
using Soju.WabiSabi.Client.StatusChangedEvents;
using Soju.WabiSabi.Models;
using Soju.Wallets;

namespace Soju.WabiSabi.Client;

public class CoinJoinClientContext
{
	public CoinJoinClient CoinJoinClient;
	public Wallet Wallet;
	public string WalletName;
	
	public ImmutableArray<AliceClient> RegisteredAliceClients;
	public ImmutableArray<TxOut> WantedOutputs;
	
	public CoinJoinClientContext(
		Wallet wallet,
		CoinJoinClient cjClient)
	{
		CoinJoinClient = cjClient;
		Wallet = wallet;
		WalletName = wallet.WalletName;
	}
	
	public SmartCoin[] StartRoundAndGetCoins(RoundState roundState)
	{
		if (Wallet.IsWalletPrivate())
		{
			Wallet.LogTrace("All mixed!");
			
			throw new CoinJoinClientException(CoinjoinError.AllCoinsPrivate);
		}
		
		SmartCoin[] coinCandidates = new CoinsView(Wallet.GetCoinjoinCoinCandidates())
			.Available()
			.ToArray();
		
		// TODO: IMPORTANT: StopWhenAllMixed
		if (coinCandidates.All(x => x.IsPrivate(Wallet.AnonScoreTarget)))
		{
			throw new CoinJoinClientException(CoinjoinError.NoCoinsEligibleToMix, $"All coin candidates are already private");
		}
		
		IEnumerable<SmartCoin> coins = CoinJoinClient.StartCoinJoin(roundState, coinCandidates);
		
		return coins.ToArray();
	}
}