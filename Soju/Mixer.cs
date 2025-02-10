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
        var transaction = new DumbTransaction(null, null);
        transaction.IsWasabi2Cj = true;

        // Select input coins from wallets
        foreach (var wallet in wallets) 
        {
            var coinCandidates = wallet.GetCoinJoinCoinCandidates();
            
            var coinSelector = CoinJoinCoinSelector.FromWallet(wallet);
            var selectedCoins = coinSelector.SelectCoinsForRound(coinCandidates, SelectionParams, wallet.LiquidityClue).ToHashSet();
            foreach (var coin in selectedCoins)
            {
                transaction.TryAddInput(wallet.WalletId, coin);
            }
        }

        // Select outputs for each wallet
        uint outputIndex = 0;
        foreach (var wallet in wallets)
        {
            var myInputsEffectiveValues = transaction.Inputs[wallet.WalletId].Select(coin => coin.EffectiveValue(RoundParams.MiningFeeRate));
            var othersInputsEffectiveValues = transaction.Inputs.Where(dictEntry => dictEntry.Key != wallet.WalletId).SelectMany(dictEntry => dictEntry.Value.Select(coin => coin.EffectiveValue(RoundParams.MiningFeeRate)));
            var availableVsize = transaction.Inputs[wallet.WalletId].Sum(coin => RoundParams.MaxVsizeCredentialValue - coin.ScriptType.EstimateInputVsize());

            var outputs = wallet.OutputProvider.GetOutputs(RoundParams, myInputsEffectiveValues, othersInputsEffectiveValues, availableVsize);

            // Add outputs as output coins to the transaction
            HashSet<DumbCoin> newCoins = [];
            foreach (var output in outputs.ToList())
            {
                transaction.TryAddOutput(wallet.WalletId, OutputToCoin(output, transaction, outputIndex));
                outputIndex++;
            }
            // Remove old coins and add new coins to the wallet
            wallet.RemoveCoins(transaction.Inputs[wallet.WalletId]);
            wallet.AddCoins(newCoins);
        }

        return new CoinjoinResult(transaction, roundId);
    }

    private static DumbCoin OutputToCoin(Output output, DumbTransaction transaction, uint outputIndex)
    {
        return new DumbCoin(transaction, output.EffectiveAmount, output.ScriptType, 1.0, outputIndex);
    }
}