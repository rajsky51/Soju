using System.Diagnostics;

using NBitcoin;
using Soju;
using Soju.Analysis;
using Soju.Extensions;
using Soju.Randomness;

var cjSkipFactors = CoinjoinSkipFactors.NoSkip;
ScriptType[] allowedScriptTypes = [ScriptType.Taproot, ScriptType.P2WPKH];
FeeRate miningFeeRate = new(Money.Satoshis(20_000));

// Generate wallets with randomly selected coins from the samples file
DumbCoinHistoryGenerator coinHistoryGenerator = new(new MoneyRange(Money.Coins(0.0002m), Money.Coins(0.1m)), miningFeeRate, allowedScriptTypes);
const int nWallets = 20;
const int newCoinHistoryDepth = 4;
SecureRandom secureRandom = SecureRandom.Instance;
Money liquidityClue = Money.Coins(10.0m);
decimal[] sampleAmounts = Sample.Amounts;
List<Wallet> wallets = new(nWallets);
for (int i = 0; i < nWallets; i++)
{
    Wallet wallet = new("wallet-" + i, liquidityClue, cjSkipFactors);

    const int nWalletCoins = 20;
    decimal[] randomAmounts = sampleAmounts.RandomElements(nWalletCoins);
    DumbCoin[] randomCoins = new DumbCoin[nWalletCoins];
    for (int j = 0; j < randomAmounts.Count(); j++)
    {
        DumbTransaction coinTx = new();
        randomCoins[j] = coinTx.AddOutputCoin(Money.Coins(randomAmounts[j]),
            allowedScriptTypes.RandomElement(secureRandom), 1.0, wallet.WalletId);
        coinHistoryGenerator.GenerateFakeHistory(randomCoins[j], newCoinHistoryDepth);
    }
    
    wallet.AddCoins(randomCoins);
    wallets.Add(wallet);
}

JSONBuilder jsonBuilder = new(wallets, "    "); // Indentation is 4 spaces
StreamWriter jsonFile = new("../coinjoins.json", false); // Always creates the file
BlockchainAnalyzer bcAnalyzer = new();

for (int i = 0; i < 10; i++) 
{
    Console.WriteLine(i);

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

    CoinjoinResult result = mixer.CompleteMix(wallets);

    bcAnalyzer.Analyze(result.Transaction);

    string coinjoinJSON = jsonBuilder.CoinjoinResultsToJSON([result], 0);
    
    jsonFile.WriteLine(coinjoinJSON);
}

jsonFile.Close();
