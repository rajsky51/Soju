using NBitcoin;
using System.Collections.Concurrent;
using System.Diagnostics;
using Soju.Blockchain.Analysis;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.Transactions;
using Soju.Helpers;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client.CoinJoin;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Client.CoinJoin.Client.Decomposer;
using Soju.Wallets;

namespace Soju;

public class  Mixer : IMixer
{
    public UtxoSelectionParameters SelectionParams { get; set; }
    public RoundParameters RoundParams { get; set; }
    private readonly BlockchainAnalyzer _bcAnalyzer;

    public Mixer(RoundParameters roundParams)
    {
        RoundParams = roundParams;
        SelectionParams = UtxoSelectionParameters.FromRoundParameters(roundParams, OutputProvider.DefaultSupportedScriptTypes);
        // CHECK: Look more into why BlockchainAnalyzer isn't a static class.
        _bcAnalyzer = new BlockchainAnalyzer();
    }

    public CoinjoinResult CompleteMix(IEnumerable<IWallet> participatingWallets)
    {
        HashSet<Wallet> wallets = participatingWallets.Cast<Wallet>().ToHashSet();
        uint256 roundId = RandomUtils.GetUInt256();
        DumbTransaction transaction = new();

        ConcurrentBag<(IWallet wallet, CoinJoinClientException exception)> clientExceptions = [];
        Console.WriteLine($"We have {wallets.Count()} wallets total.");
        Stopwatch sw = new();
        sw.Start();
        // NOTE: Select input coins from wallets
        Parallel.ForEach(wallets, wallet =>
        {
            CoinJoinConfiguration coinJoinConfiguration = new("foo", 1_000_000, Constants.AbsoluteMinInputCount, false);
            CoinJoinClient coinJoinClient = new(wallet.OutputProvider, CoinJoinCoinSelector.FromWallet(wallet), coinJoinConfiguration);
            // var coinCandidates = wallet.GetCoinJoinCoinCandidates();
            // var coinSelector = CoinJoinCoinSelector.FromWallet(wallet);
            // var selectedCoins = coinSelector.SelectCoinsForRound(coinCandidates, SelectionParams, wallet.LiquidityClue).ToHashSet();
            HashSet<DumbCoin> selectedCoins = [];
            try
            {
                selectedCoins =
                    coinJoinClient.StartCoinJoin(wallet, true, RoundParams).ToHashSet();
                
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

        sw.Restart();
        // NOTE: Select outputs for each wallet
        ConcurrentBag<(WalletId WalletId, WabiSabi.Client.CoinJoin.Client.Decomposer.Output Output)> outputsWithIds = [];

        Parallel.ForEach(wallets, wallet =>
        {
            if (!transaction.Inputs.TryGetValue(wallet.WalletId, out HashSet<DumbCoin>? walletCoinsInTransaction))
            {
                // NOTE: Wallet has no registered inputs in this transaction
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
                // NOTE: The wallet couldn't select any outputs so we need to unregister all its inputs from the coinjoin
                transaction.RemoveWalletInputs(wallet.WalletId);
                return;
            }

            foreach (var output in walletOutputs)
            {
                outputsWithIds.Add((wallet.WalletId, output));
            }
        });

        // NOTE: Add outputs as output coins to the transaction
        outputsWithIds
            .OrderByDescending(x => x.Output.Amount)
            .ToList()
            .ForEach(x => AddOutputToTransaction(transaction, x.Output, x.WalletId));

        // NOTE: Remove old coins and add new coins to wallets
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
        
        // CHECK: Setting new anonscores to every coin in the transaction.
        // Before it was done outside of mixer. Maybe if it should be outside
        // again. But I guess not, because now we can calculate anonscore gains
        // and put them into the CoinjoinResult if we wish to.
        _bcAnalyzer.Analyze(transaction);

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