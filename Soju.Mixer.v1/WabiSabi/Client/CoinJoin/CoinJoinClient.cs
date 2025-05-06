using System.Collections.Immutable;
using NBitcoin;
using Soju.Blockchain.TransactionOutputs;
using Soju.Crypto.Randomness;
using Soju.Models;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Client.CoinJoin.Client.Decomposer;
using Soju.WabiSabi.Client.CoinJoin.Manager.StatusChangedEvents;
using Soju.Wallets;

namespace Soju.WabiSabi.Client.CoinJoin;

public class CoinJoinClient
{
    private static readonly Money MinimumOutputAmountSanity = Money.Coins(0.0001m); // ignore rounds with too big minimum denominations
    
    private readonly SecureRandom _secureRandom;
    private readonly OutputProvider _outputProvider;
    private readonly CoinJoinConfiguration _coinJoinConfiguration;
    private readonly CoinJoinCoinSelector _coinJoinCoinSelector;
    private readonly CoinjoinSkipFactors _skipFactors;

    public CoinJoinClient(
        OutputProvider outputProvider,
        CoinJoinCoinSelector coinJoinCoinSelector,
        CoinJoinConfiguration coinJoinConfiguration,
        CoinjoinSkipFactors? skipFactors = null)
    {
        _outputProvider = outputProvider;
        _coinJoinConfiguration = coinJoinConfiguration;
        _coinJoinCoinSelector = coinJoinCoinSelector;
        _skipFactors = skipFactors ?? CoinjoinSkipFactors.NoSkip;
        _secureRandom = new SecureRandom();
    }

    public IEnumerable<DumbCoin> StartCoinJoin(IWallet wallet, bool stopWhenAllMixed, RoundParameters roundParameters)
    {
        if (!IsRoundEconomic(roundParameters.MiningFeeRate))
        {
            throw new CoinJoinClientException(CoinjoinError.UneconomicalRound, "Uneconomical round skipped.");
        }
        if (roundParameters.MiningFeeRate.SatoshiPerByte > _coinJoinConfiguration.MaxCoinJoinMiningFeeRate)
        {
            string roundSkippedMessage =
                $"Mining fee rate was {roundParameters.MiningFeeRate} but max allowed is {_coinJoinConfiguration.MaxCoinJoinMiningFeeRate}.";
            throw new CoinJoinClientException(CoinjoinError.MiningFeeRateTooHigh, roundSkippedMessage);
        }
        if (roundParameters.MinInputCountByRound < _coinJoinConfiguration.AbsoluteMinInputCount)
        {
            string roundSkippedMessage = 
                $"Min input count for the round was {roundParameters.MinInputCountByRound} but min allowed is {_coinJoinConfiguration.AbsoluteMinInputCount}.";
            throw new CoinJoinClientException(CoinjoinError.MinInputCountTooLow, roundSkippedMessage);
        }
        // if (_skipFactors.ShouldSkipRoundRandomly(_secureRandom, roundParameters.MiningFeeRate, _roundStatusUpdater.CoinJoinFeeRateMedians, currentRoundState.Id))
        // {
        //     string roundSkippedMessage = "Round skipped randomly for better privacy.";
        //     currentRoundState.LogInfo(roundSkippedMessage);
        //     throw new CoinJoinClientException(CoinjoinError.RandomlySkippedRound, roundSkippedMessage);
        // }
        
        IEnumerable<DumbCoin> coinCandidates = wallet.GetCoinJoinCoinCandidates();

        // IMPORTANT: Hack
        Money liquidityClue = Money.Coins(10.0m);
        UtxoSelectionParameters utxoSelectionParameters = UtxoSelectionParameters.FromRoundParameters(roundParameters, _outputProvider.SupportedScriptTypes);
        ImmutableList<DumbCoin> coins = _coinJoinCoinSelector.SelectCoinsForRound(coinCandidates, utxoSelectionParameters, liquidityClue);
        
        if (coins.IsEmpty)
        {
            throw new CoinJoinClientException(CoinjoinError.NoCoinsEligibleToMix, $"No coin was selected from '{coinCandidates.Count()}' number of coins. Probably it was not economical, total amount of coins were: {Money.Satoshis(coinCandidates.Sum(c => c.Amount))} BTC.");
        }

        return coinCandidates;
    }

    internal static bool IsRoundEconomic(FeeRate roundFeeRate)
    {
        return true;
    }
}

public record CoinJoinConfiguration(string CoordinatorIdentifier,  decimal MaxCoinJoinMiningFeeRate, int AbsoluteMinInputCount, bool AllowSoloCoinjoining);