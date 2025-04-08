using NBitcoin;
using System.Collections.Concurrent;
using System.Diagnostics;
using Soju.Decomposer;
using Soju.Wallets;

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

    public CoinjoinResult CompleteMix(IReadOnlyList<IWallet> wallets)
    {
        var roundId = RandomUtils.GetUInt256();
        DumbTransaction transaction = new();

        ConcurrentBag<(IWallet wallet, CoinJoinClientException exception)> clientExceptions = [];
        Console.WriteLine($"We have {wallets.Count()} wallets total.");
        Stopwatch sw = new();
        sw.Start();
        // Select input coins from wallets
        Parallel.ForEach(wallets, wallet =>
        {
            CoinJoinConfiguration coinJoinConfiguration = new("foo", 1_000_000, Constants.AbsoluteMinInputCount, false);
            CoinJoinClient coinJoinClient = new(wallet.OutputProvider, CoinJoinCoinSelector.FromWallet(wallet), coinJoinConfiguration);
            var coinCandidates = wallet.GetCoinJoinCoinCandidates();
            var coinSelector = CoinJoinCoinSelector.FromWallet(wallet);
            // var selectedCoins = coinSelector.SelectCoinsForRound(coinCandidates, SelectionParams, wallet.LiquidityClue).ToHashSet();
            IReadOnlyCollection<DumbCoin> selectedCoins = new HashSet<DumbCoin>();
            try
            {
                selectedCoins =
                    coinJoinClient.StartCoinJoinAsync(wallet, true, RoundParams).ToHashSet();
                
                foreach (var coin in selectedCoins)
                {
                    transaction.TryAddInput(coin);
                }
            }
            catch (CoinJoinClientException e)
            {
                clientExceptions.Add((wallet, e));
            }

            Console.WriteLine($"{wallet.WalletId} : selected {selectedCoins.Count} coins.");
        });
        sw.Stop();
        
        Console.WriteLine($"Choosing inputs took: {sw.Elapsed}. That is {sw.Elapsed / wallets.Count()} per wallet.");
        Console.WriteLine("Client exceptions:");
        foreach (var e in clientExceptions)
        {
            Console.WriteLine($"{e.wallet.WalletId} exception: {e.exception.Message}");
        }

        // Select outputs for each wallet
        sw.Restart();

        ConcurrentBag<(WalletId WalletId, Output Output)> outputsWithIds = [];

        Parallel.ForEach(wallets, wallet =>
        {
            if (!transaction.Inputs.TryGetValue(wallet.WalletId, out HashSet<DumbCoin>? walletCoinsInTransaction))
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

            var availableVsize = transaction.Inputs[wallet.WalletId].Sum(coin =>
                RoundParams.MaxVsizeCredentialValue - coin.ScriptType.EstimateInputVsize());

            Output[] walletOutputs;
            try
            {
                walletOutputs = wallet.OutputProvider.GetOutputs(RoundParams, myInputsEffectiveValues,
                    othersInputsEffectiveValues, availableVsize).ToArray();
            }
            catch (InvalidOperationException e)
            {
                // The wallet couldn't select any outputs so we need to unregister all its inputs from the coinjoin
                transaction.RemoveWalletInputs(wallet.WalletId);
                return;
            }

            foreach (var output in walletOutputs)
            {
                outputsWithIds.Add((wallet.WalletId, output));
            }
        });

        // Add outputs as output coins to the transaction
        outputsWithIds
            .OrderByDescending(x => x.Output.Amount)
            .ToList()
            .ForEach(x => AddOutputToTransaction(transaction, x.Output, x.WalletId));

        // Remove old coins and add new coins to wallets
        foreach (var wallet in wallets)
        {
            if (transaction.Outputs.ContainsKey(wallet.WalletId)) 
            {
                wallet.RemoveCoins(transaction.Inputs[wallet.WalletId]);
                wallet.AddCoins(transaction.Outputs[wallet.WalletId]);
            }
        }
        
        sw.Stop();
        Console.WriteLine($"Choosing outputs took: {sw.Elapsed}. That is {sw.Elapsed / wallets.Count()} per wallet.");

        Money totalInputsAmount = transaction.Inputs.SelectMany(kvp => kvp.Value).Sum(coin => coin.Amount);
        Money totalOutputsAmount = transaction.Outputs.SelectMany(kvp => kvp.Value).Sum(coin => coin.Amount); 
        Money coordinationFee = CalculateCoordinationFee(RoundParams, transaction, ScriptType.P2WPKH);
        Money miningFee = totalInputsAmount - totalOutputsAmount - coordinationFee;

        return new CoinjoinResult(transaction, roundId, miningFee, coordinationFee);
    }

    private static DumbCoin AddOutputToTransaction(DumbTransaction transaction, Output output, WalletId walletId)
    {
        return transaction.AddOutputCoin(output.EffectiveAmount, output.ScriptType, 1.0, walletId);
    }

    private static Money CalculateCoordinationFee(RoundParameters roundParameters, DumbTransaction tx, ScriptType coordinatorScriptType)
    {
        int sizeToPayFor = tx.EstimateVSize() + coordinatorScriptType.EstimateOutputVsize();
        Money miningFee = roundParameters.MiningFeeRate.GetFee(sizeToPayFor) + Money.Satoshis(1);

        Money totalInputsAmount = tx.Inputs.SelectMany(kvp => kvp.Value).Sum(coin => coin.Amount);
        Money totalOutputsAmount = tx.Outputs.SelectMany(kvp => kvp.Value).Sum(coin => coin.Amount);
        Money balance = totalInputsAmount - totalOutputsAmount;
        Money availableCoordinationFee = balance - miningFee;

        Money minEconomicalOutput = roundParameters.MiningFeeRate.GetFee(coordinatorScriptType.EstimateOutputVsize()) +
                                  new FeeRate(1.0m).GetFee(coordinatorScriptType.EstimateInputVsize());

        return availableCoordinationFee > minEconomicalOutput ? availableCoordinationFee : Money.Zero;
    }
}