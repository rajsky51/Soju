using NBitcoin;
using Soju.Blockchain.Keys;
using Soju.Blockchain.TransactionOutputs;
using Soju.Helpers;
using Soju.Models;
using Soju.WabiSabi.Client;

namespace Soju.Wallets;

public class Wallet : IWallet
{
    public string WalletName { get; }
    public WalletId WalletId { get; }
    public bool IsUnderPlebStop => Coins.TotalAmount() <= KeyManager.DefaultPlebStopThreshold;

    public OutputProvider OutputProvider { get; }

    public int AnonScoreTarget { get; }
    public bool ConsolidationMode { get; set; }
    public bool RedCoinIsolation { get; }
    public CoinjoinSkipFactors CoinjoinSkipFactors { get; }

    public HashSet<DumbCoin> Coins = [];
    
    public Wallet(
        string walletName,
        int anonScoreTarget,
        CoinjoinSkipFactors cjSkipFactors)
    {
        WalletName = walletName;
        WalletId = new WalletId(Guid.NewGuid());
        OutputProvider = new OutputProvider();
        AnonScoreTarget = anonScoreTarget;
        ConsolidationMode = false;
        RedCoinIsolation = false;
        CoinjoinSkipFactors = cjSkipFactors;
    }
    
    private double GetPrivacyPercentage(HashSet<DumbCoin> coins, int privateThreshold)
    {
        var privateAmount = coins.Where(x => x.IsPrivate(privateThreshold)).TotalAmount();
        var normalAmount = coins.Where(x => !x.IsPrivate(privateThreshold)).TotalAmount();

        var privateDecimalAmount = privateAmount.ToDecimal(MoneyUnit.BTC);
        var normalDecimalAmount = normalAmount.ToDecimal(MoneyUnit.BTC);
        var totalDecimalAmount = privateDecimalAmount + normalDecimalAmount;

        var pcPrivate = totalDecimalAmount == 0M ? 1d : (double)(privateDecimalAmount / totalDecimalAmount);
        return pcPrivate;
    }
    
    public bool IsWalletPrivate() => GetPrivacyPercentage(Coins, AnonScoreTarget) >= 1;
    
    public IEnumerable<DumbCoin> GetCoinJoinCoinCandidates() => Coins;
    
    public int RemoveCoins(IEnumerable<DumbCoin> coinsToRemove)
    {
        var nRemoved = 0;
        foreach (var coin in coinsToRemove)
        {
            if (Coins.Remove(coin)) nRemoved++;
        }
        return nRemoved;
    }

    public int AddCoins(IEnumerable<DumbCoin> coinsToAdd)
    {
        int nAdded = 0;
        foreach (var coin in coinsToAdd) 
        {
            if (Coins.Add(coin)) nAdded++;            
        }
        return nAdded;
    }
}