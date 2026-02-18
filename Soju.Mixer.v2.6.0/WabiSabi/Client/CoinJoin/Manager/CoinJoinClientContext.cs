using System.Collections.Immutable;
using System.Diagnostics;
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
	
	private record CoinSelectionResult(SmartCoin[] CandidateCoins, SmartCoin[] BannedCoins, SmartCoin[] ImmatureCoins, SmartCoin[] UnconfirmedCoins, SmartCoin[] ExcludedCoins)
	{
		public CoinSelectionResult() : this([], [], [], [], []) { }
	}
	
	private CoinSelectionResult GetCoinSelection()
	{
		SmartCoin[] coinCandidates = new CoinsView(Wallet.GetCoinjoinCoinCandidates())
			.Available()
			.ToArray();
		
		if (coinCandidates.Length == 0)
		{
			return new CoinSelectionResult();
		}
		
		return new CoinSelectionResult(coinCandidates, [], [], [], []);
	}
	
	public SmartCoin[] StartRoundAndGetCoins(RoundState roundState)
	{
		CoinSelectionResult result = GetCoinSelection();
		Debug.Assert(result.BannedCoins.Length      == 0
		          && result.ImmatureCoins.Length    == 0
		          && result.UnconfirmedCoins.Length == 0
		          && result.ExcludedCoins.Length    == 0);
		
		SmartCoin[] coinCandidates = result.CandidateCoins;
		
		// TODO: implement pleb stop threshold and override pleb stop
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

public record CoinJoinConfiguration(string CoordinatorIdentifier,  decimal MaxCoinJoinMiningFeeRate, int AbsoluteMinInputCount, bool AllowSoloCoinjoining);
