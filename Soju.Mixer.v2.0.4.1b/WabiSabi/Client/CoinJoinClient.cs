using NBitcoin;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WabiSabi.Crypto.Randomness;
using Soju.Blockchain.TransactionOutputs;
using Soju.Extensions;
using Soju.Logging;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Client.CoinJoin.Manager.StatusChangedEvents;
using Soju.Wallets;

namespace Soju.WabiSabi.Client;

public class CoinJoinClient
{
	private static readonly Money
		MinimumOutputAmountSanity = Money.Coins(0.0001m); // ignore rounds with too big minimum denominations

	public CoinJoinClient(
		OutputProvider outputProvider,
		string coordinatorIdentifier,
		CoinJoinCoinSelector coinJoinCoinSelector)
	{
		OutputProvider = outputProvider;
		CoordinatorIdentifier = coordinatorIdentifier;
		CoinJoinCoinSelector = coinJoinCoinSelector;
		SecureRandom = new SecureRandom();
	}

	private SecureRandom SecureRandom { get; }
	private OutputProvider OutputProvider { get; }
	private string CoordinatorIdentifier { get; }
	private CoinJoinCoinSelector CoinJoinCoinSelector { get; }
	private TimeSpan DoNotRegisterInLastMinuteTimeLimit { get; }

	private TimeSpan FeeRateMedianTimeFrame { get; }
	private TimeSpan MaxWaitingTimeForRound { get; } = TimeSpan.FromMinutes(10);
	
	public IEnumerable<DumbCoin> StartCoinJoin(IWallet wallet, RoundParameters roundParameters)
	{
		IEnumerable<DumbCoin> coinCandidates = wallet.GetCoinJoinCoinCandidates();
		
		// TODO: Hack
		var liquidityClue = Money.Coins(10.0m);
		var utxoSelectionParameters = UtxoSelectionParameters.FromRoundParameters(roundParameters);

		ImmutableList<DumbCoin> coins = CoinJoinCoinSelector.SelectCoinsForRound(coinCandidates, utxoSelectionParameters, liquidityClue);
		
		if (roundParameters.AllowedOutputAmounts.Min < MinimumOutputAmountSanity)
		{
			string roundSkippedMessage = "Abandoning: the minimum output amount is too high.";
			throw new CoinJoinClientException(CoinjoinError.MinOutputAmountTooHigh, roundSkippedMessage);
		}
		
		if (!roundParameters.AllowedInputTypes.Contains(ScriptType.P2WPKH) || !roundParameters.AllowedOutputTypes.Contains(ScriptType.P2WPKH))
		{
			// NOTE: In the original code there the round is marked as 
			// excluded, but we are not in async nor in a loop so we can just
			// send no coins
			coins = coins.Clear();
		}

		if (roundParameters.MaxSuggestedAmount != default && coins.Any(c => c.Amount > roundParameters.MaxSuggestedAmount))
		{
			coins = coins.Clear();
		}

		if (coins.IsEmpty)
		{
			throw new CoinJoinClientException(CoinjoinError.NoCoinsEligibleToMix, $"No coin was selected from '{coinCandidates.Count()}' number of coins. Probably it was not economical, total amount of coins were: {Money.Satoshis(coinCandidates.Sum(c => c.Amount))} BTC.");
		}

		return coins;
	}
}