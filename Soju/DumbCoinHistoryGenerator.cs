using NBitcoin;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.Transactions;
using Soju.Crypto.Randomness;
using Soju.Extensions;
using Soju.Helpers;
using Soju.WabiSabi.Models;
using Soju.Wallets;

namespace Soju;

public class DumbCoinHistoryGenerator
{
    public MoneyRange InputAmountRange;
    public FeeRate FeeRate;
    public ScriptType[] AllowedScripts;
    private readonly InsecureRandom _rng;
    
    public DumbCoinHistoryGenerator(MoneyRange inputAmountRnage, FeeRate feeRate, ScriptType[] allowedScripts)
    {
        InputAmountRange = inputAmountRnage;
        FeeRate = feeRate;
        AllowedScripts = allowedScripts;

        _rng = InsecureRandom.Instance;
    }
    
    public void GenerateFakeHistory(DumbCoin coin, int recursions)
    {
        if (recursions == 0) return;
    
        DumbTransaction tx = coin.Transaction;
        Money totalOutputAmount = tx.Outputs
            .SelectMany(kvp => kvp.Value)
            .TotalAmount();
        Money totalInputAmount = tx.Inputs
            .SelectMany(kvp => kvp.Value)
            .TotalAmount();
    
        Money totalOutputFee = tx.Outputs
            .SelectMany(kvp => kvp.Value)
            .Sum(output => FeeRate.GetFee(output.ScriptType.EstimateOutputVsize()));
        Money totalInputFee = tx.Inputs
            .SelectMany(kvp => kvp.Value)
            .Sum(input => FeeRate.GetFee(input.ScriptType.EstimateInputVsize()));

        Money neededFunds = totalOutputAmount + totalOutputFee + totalInputFee - totalInputAmount;
    
        while (neededFunds > Money.Zero)
        {
            // Need a new input
            DumbTransaction inputTx = new();
        
            ScriptType inputScript = AllowedScripts.RandomElement(_rng);
            Money inputRawAmount = MyExtensions.GetRandomMoney(_rng, InputAmountRange);
            WalletId inputWalletId = coin.WalletId; //_rng.GetBool() ? coin.WalletId : new WalletId(Guid.NewGuid());
            DumbCoin newInputCoin = inputTx.AddOutputCoin(inputRawAmount + FeeRate.GetFee(inputScript.EstimateOutputVsize()), inputScript, 1.0, inputWalletId);
        
            tx.TryAddInput(newInputCoin);
            neededFunds -= inputRawAmount;
        }

        foreach (DumbCoin inputCoin in tx.Inputs.SelectMany(kvp => kvp.Value))
        {
            GenerateFakeHistory(inputCoin, recursions - 1);
        }
    }
}
