using System.Text.Json;
using NBitcoin;
using WabiSabi.Crypto.Randomness;
using Soju;
using Soju.Extensions;

public class CoinjoinJSON

var cjSkipFactors = CoinjoinSkipFactors.NoSkip;
ScriptType[] allowedScriptTypes = [ScriptType.Taproot, ScriptType.P2WPKH];

// Generate wallets with randomly selected coins
var liquidityClue = Money.Coins(10.0m);
var sampleAmounts = Sample.Amounts;
List<Wallet> wallets = [];
for (int i = 0; i < 5; i++)
{
    Wallet wallet = new("wallet-" + i, liquidityClue, cjSkipFactors);
    
    var randomCoins = sampleAmounts
        .RandomElements(50)
        .Select(x => new DumbCoin(null, Money.Coins(x), 
            allowedScriptTypes.RandomElement(SecureRandom.Instance), 1.0, RandomUtils.GetUInt32() % 2));
    
    wallet.AddCoins(randomCoins);
    wallets.Add(wallet);
}

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
		allowedOutputTypes   : [.. allowedScriptTypes]);

    Mixer mixer = new(selectionParams, roundParams);

    var result = mixer.CompleteMix(wallets);

    var coinjoins = new Dictionary<string, object>
    {
        [result.RoundId.ToString()] = new
        {
            txid = result.Transaction.Id,
            inputs = new Dictionary<string, object>
            foreach (var input in result.Transaction.Inputs)
            {
                inputs.add(input);
            }
        }
    };
}