using System.Collections.Concurrent;
using System.Diagnostics;

using NBitcoin;

namespace Soju;

public class Mixer 
{
    public UtxoSelectionParameters SelectionParams { get; set; }
    public RoundParameters RoundParams { get; set; }

    public Mixer(UtxoSelectionParameters selectionParams, RoundParameters roundParams)
    {
        SelectionParams = selectionParams;
        RoundParams = roundParams;
    }

    public CoinjoinResult CompleteMix(IEnumerable<IWallet> wallets)
    {
        var roundId = RandomUtils.GetUInt256();
        DumbTransaction transaction = new(null, null);
        transaction.IsWasabi2Cj = true;

        Debug.WriteLine($"We have {wallets.Count()} wallets total.");
        Stopwatch sw = new();
        sw.Start();
        // Select input coins from wallets
        Parallel.ForEach(wallets, wallet =>
        {
            var coinCandidates = wallet.GetCoinJoinCoinCandidates();
            
            var coinSelector = CoinJoinCoinSelector.FromWallet(wallet);
            var selectedCoins = coinSelector.SelectCoinsForRound(coinCandidates, SelectionParams, wallet.LiquidityClue).ToHashSet();
            foreach (var coin in selectedCoins)
            {
                transaction.TryAddInput(wallet.WalletId, coin);
            }

            Debug.WriteLine($"{wallet.WalletId} : selected {selectedCoins.Count} coins.");
        });
        sw.Stop();
        Debug.WriteLine($"Choosing inputs took: {sw.Elapsed}. That is {sw.Elapsed / wallets.Count()} per wallet.");
        
        // Select outputs for each wallet
        sw.Restart();

        ConcurrentBag<(WalletId WalletId, Output Output)> outputsWithIds = [];

        Parallel.ForEach(wallets, wallet =>
        {
            HashSet<DumbCoin>? walletCoinsInTransaction = [];
            if (!transaction.Inputs.TryGetValue(wallet.WalletId, out walletCoinsInTransaction))
            {
                // Wallet has no registered inputs in this transaction
                return;
            }
            var myInputsEffectiveValues = walletCoinsInTransaction
                .Select(coin => coin
                .EffectiveValue(RoundParams.MiningFeeRate));

            var othersInputsEffectiveValues = transaction.Inputs
                .Where(entry => entry.Key != wallet.WalletId)
                .SelectMany(entry => entry.Value
                .Select(coin => coin.EffectiveValue(RoundParams.MiningFeeRate)));

            var availableVsize = transaction.Inputs[wallet.WalletId].Sum(coin => RoundParams.MaxVsizeCredentialValue - coin.ScriptType.EstimateInputVsize());

            var walletOutputs = wallet.OutputProvider.GetOutputs(RoundParams, myInputsEffectiveValues, othersInputsEffectiveValues, availableVsize);
            foreach (var output in walletOutputs)
            {
                outputsWithIds.Add((wallet.WalletId, output));
            }
        });

        // Add outputs as output coins to the transaction
        outputsWithIds
            .OrderByDescending(x => x.Output.Amount)
            .Select((x, i) => (x.WalletId, Coin: OutputToCoin(x.Output, transaction, (uint)i)))
            .ToList()
            .ForEach(x => transaction.TryAddOutput(x.WalletId, x.Coin));

        // Remove old coins and add new coins to wallets
        foreach (var wallet in wallets)
        {
            wallet.RemoveCoins(transaction.Inputs[wallet.WalletId]);
            wallet.AddCoins(transaction.Outputs[wallet.WalletId]);
        }
        
        sw.Stop();
        Debug.WriteLine($"Choosing outputs took: {sw.Elapsed}. That is {sw.Elapsed / wallets.Count()} per wallet.");

        return new CoinjoinResult(transaction, roundId);
    }

    private static DumbCoin OutputToCoin(Output output, DumbTransaction transaction, uint outputIndex)
    {
        return new DumbCoin(transaction, output.EffectiveAmount, output.ScriptType, 1.0, outputIndex);
    }
}