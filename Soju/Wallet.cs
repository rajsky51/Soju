using NBitcoin;
using Soju.Helpers;

namespace Soju;

public class Wallet : IWallet 
{
    public string WalletName { get; }
    public WalletId WalletId { get; }
    public bool IsUnderPlebStop => Coins.TotalAmount() <= Constants.DefaultPlebStopThreshold;
    public bool IsMixable { get; }

    public OutputProvider OutputProvider { get; }

    public int AnonScoreTarget { get; }
    public bool ConsolidationMode { get; set; }
    public bool RedCoinIsolation { get; }
    public CoinjoinSkipFactors CoinjoinSkipFactors { get; }

    public Money LiquidityClue { get; }

    public HashSet<DumbCoin> Coins = [];

    public Wallet(string walletName, Money liquidityClue, CoinjoinSkipFactors cjSkipFactors)
    {
        WalletName = walletName;
        WalletId = new WalletId(Guid.NewGuid());
        IsMixable = true;
        OutputProvider = new OutputProvider();
        AnonScoreTarget = Constants.DefaultAnonScoreTarget;
        ConsolidationMode = false;
        RedCoinIsolation = false;
        CoinjoinSkipFactors = cjSkipFactors;
        LiquidityClue = liquidityClue;
    }

    public int GetPrivacyPercentage()
	{
		var currentPrivacyScore = Coins.Sum(x => x.Amount.Satoshi * Math.Min(x.AnonymitySet - 1, x.IsPrivate(AnonScoreTarget) ? AnonScoreTarget - 1 : AnonScoreTarget - 2));
		var maxPrivacyScore = Coins.TotalAmount().Satoshi * (AnonScoreTarget - 1);
		int pcPrivate = maxPrivacyScore == 0M ? 0 : (int)(currentPrivacyScore * 100 / maxPrivacyScore);

		return pcPrivate;
	}
    public bool IsWalletPrivate() => GetPrivacyPercentage() >= 100;

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