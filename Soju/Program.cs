using NBitcoin;
using WabiSabi.Crypto.Randomness;
using Soju;
using Soju.Analysis;
using Soju.Extensions;

var cjSkipFactors = CoinjoinSkipFactors.NoSkip;
ScriptType[] allowedScriptTypes = [ScriptType.Taproot, ScriptType.P2WPKH];

// Generate wallets with randomly selected coins
var liquidityClue = Money.Coins(10.0m);
var sampleAmounts = Sample.Amounts;
List<Wallet> wallets = [];
for (int i = 0; i < 20; i++)
{
    Wallet wallet = new("wallet-" + i, liquidityClue, cjSkipFactors);
    
    var randomCoins = sampleAmounts
        .RandomElements(50)
        .Select(x => new DumbCoin(null, Money.Coins(x), 
            allowedScriptTypes.RandomElement(SecureRandom.Instance), 1.0, 1))
        .ToList();

    for (int j = 0; j < randomCoins.Count; j++)
    {
        // Filling the coin's transaction with dummy data
        var coin = randomCoins[j];
        var inputCoin = new DumbCoin(null, coin.Amount, allowedScriptTypes.RandomElement(SecureRandom.Instance), 1.0, 1);
        coin.Transaction.TryAddInput(wallet.WalletId, inputCoin);
        coin.Transaction.TryAddOutput(wallet.WalletId, coin);
    }
    
    wallet.AddCoins(randomCoins);
    wallets.Add(wallet);
}

JSONBuilder jsonBuilder = new(wallets, "    "); // indentation is 4 spaces
BlockchainAnalyzer bcAnalyzer = new();

for (int i = 0; i < 10; i++) 
{
    Console.WriteLine(i);

    FeeRate miningFeeRate = new(Money.Satoshis(20_000));
    MoneyRange allowedAmounts = new(Money.Satoshis(10_000), Money.Coins(43_000));

    UtxoSelectionParameters selectionParams = new(
        AllowedInputAmounts     : allowedAmounts, 
        MinAllowedOutputAmount  : allowedAmounts.Min,
        MiningFeeRate           : miningFeeRate,
        AllowedInputScriptTypes : [.. allowedScriptTypes]
    );

    RoundParameters roundParams = new(
        miningFeeRate        : miningFeeRate, 
        maxSuggestedAmount   : Money.Coins(43_000),
		minInputCountByRound : 10,
		maxInputCountByRound : 500,
		allowedInputAmounts  : allowedAmounts,
		allowedOutputAmounts : allowedAmounts,
		allowedInputTypes    : [.. allowedScriptTypes],
		allowedOutputTypes   : [.. allowedScriptTypes]
    );

    Mixer mixer = new(selectionParams, roundParams);

    var result = mixer.CompleteMix(wallets);

    bcAnalyzer.Analyze(result.Transaction);

    string coinjoinJSON = jsonBuilder.CoinjoinResultsToJSON([result], 0) + "\n";
    Console.WriteLine(coinjoinJSON);
}
