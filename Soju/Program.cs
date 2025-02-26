using System.Dynamic;
using System.Security.Authentication.ExtendedProtection;
using System.Text.Json;

using NBitcoin;
using Soju;
using Soju.Analysis;
using Soju.Extensions;
using Soju.Json;
using Soju.Randomness;

JsonSerializerOptions jsonOptions = new()
{
    AllowTrailingCommas = true,
    RespectRequiredConstructorParameters = true,
    WriteIndented = true
};

string wabiSabiConfigFileName = "Json/Test/WabiSabiConfig.json";
string wabiSabiConfigString = File.ReadAllText(wabiSabiConfigFileName);
WabiSabiConfig wabiSabiConfig = JsonSerializer.Deserialize<WabiSabiConfig>(wabiSabiConfigString, jsonOptions)!;

string scenarioFileName = "Json/Test/Scenario.json";
string scenarioString = File.ReadAllText(scenarioFileName);
CoinjoinScenario scenario = JsonSerializer.Deserialize<CoinjoinScenario>(scenarioString, jsonOptions)!;

RoundParameters roundParams = ParametersProvider.GetRoundParameters(wabiSabiConfig);
UtxoSelectionParameters utxoSelectionParams = UtxoSelectionParameters.FromRoundParameters(roundParams, roundParams.AllowedInputTypes.ToArray());
CoinjoinSkipFactors cjSkipFactors = CoinjoinSkipFactors.NoSkip;
ScriptType[] allowedScriptTypes = roundParams.AllowedInputTypes.Intersect(roundParams.AllowedOutputTypes).ToArray();

// Generate wallets according to the scenario
DumbCoinHistoryGenerator coinHistoryGenerator = new(new MoneyRange(Money.Coins(0.0002m), Money.Coins(0.1m)), roundParams.MiningFeeRate, allowedScriptTypes);
int nWallets = scenario.Wallets.Count;
const int newCoinHistoryDepth = 4;
SecureRandom secureRandom = SecureRandom.Instance;
Money liquidityClue = Money.Coins(10.0m);
List<Wallet> wallets = new(nWallets);

for (int i = 0; i < nWallets; i++)
{
    WalletConfig walletConfig = scenario.Wallets[i];
    float anonScoreTarget = scenario.DefaultAnonScoreTarget;
    if (walletConfig.AnonScoreTarget is not null) float.TryParse(walletConfig.AnonScoreTarget, out anonScoreTarget);
    Wallet wallet = new("wallet-" + i, (int)anonScoreTarget, liquidityClue, cjSkipFactors);
    List<long> funds = walletConfig.Funds;
    DumbCoin[] coins = new DumbCoin[funds.Count];
    for (int j = 0; j < funds.Count; j++)
    {
        DumbTransaction coinTx = new();
        coins[j] = coinTx.AddOutputCoin(Money.Satoshis(funds[j]),
            allowedScriptTypes.RandomElement(secureRandom), 1.0, wallet.WalletId);
        coinHistoryGenerator.GenerateFakeHistory(coins[j], newCoinHistoryDepth);
    }

    wallet.AddCoins(coins);
    wallets.Add(wallet);
}

JsonBuilder jsonBuilder = new(wallets, "    "); // 4 space indentation
StreamWriter jsonFile = new("../coinjoins.json", false); // Always create the file
BlockchainAnalyzer bcAnalyzer = new();

long nRounds = scenario.Rounds == 0 ? long.MaxValue : scenario.Rounds; // long.MaxValue is basically infinity
for (long i = 0; i < nRounds; i++) 
{
    Console.WriteLine(i);

    Mixer mixer = new(utxoSelectionParams, roundParams);

    CoinjoinResult result = mixer.CompleteMix(wallets);

    bcAnalyzer.Analyze(result.Transaction);

    string coinjoinJson = jsonBuilder.CoinjoinResultsToJson([result], 0);
    
    jsonFile.WriteLine(coinjoinJson);
}

jsonFile.Close();

return 0;