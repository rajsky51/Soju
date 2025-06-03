using System.Diagnostics;
using NBitcoin;
using System.Reflection;
using System.Text.Json;
using Soju;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.Transactions;
using Soju.Crypto.Randomness;
using Soju.Extensions;
using Soju.Json;
using Soju.Models;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client.CoinJoin;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Models;
using Soju.Wallets;

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
CoinjoinSkipFactors cjSkipFactors = CoinjoinSkipFactors.NoSkip;
ScriptType[] allowedScriptTypes = roundParams.AllowedInputTypes.Intersect(roundParams.AllowedOutputTypes).ToArray();

// NOTE: Load in the chosen version (mixer and wallet constructor)
const string pathToMixerAssembly = "/home/talar/projects/soju-experimental/Soju.Mixer.v1/bin/Debug/net9.0/Soju.Mixer.v1.dll";
Assembly assembly = Assembly.LoadFrom(pathToMixerAssembly);

IMixer mixer = (IMixer)Activator.CreateInstance(assembly.GetType("Soju.Mixer")!, roundParams)!;
Type wallet_t = assembly.GetType("Soju.Wallets.Wallet")!;
ConstructorInfo? walletConstructor = wallet_t.GetConstructor([typeof(string), typeof(int), typeof(Money), typeof(CoinjoinSkipFactors)]);
Debug.Assert(walletConstructor != null);


// NOTE: Generate wallets according to the scenario
DumbCoinHistoryGenerator coinHistoryGenerator = new(new MoneyRange(Money.Coins(0.0002m), Money.Coins(0.1m)), roundParams.MiningFeeRate, allowedScriptTypes);
int nWallets = scenario.Wallets.Count;
const int newCoinHistoryDepth = 4;
SecureRandom secureRandom = SecureRandom.Instance;
Money liquidityClue = Money.Coins(10.0m);
List<IWallet> wallets = new(nWallets);

for (int i = 0; i < nWallets; i++)
{
    WalletConfig walletConfig = scenario.Wallets[i];
    float anonScoreTarget = scenario.DefaultAnonScoreTarget;
    if (walletConfig.AnonScoreTarget is not null) float.TryParse(walletConfig.AnonScoreTarget, out anonScoreTarget);
    IWallet wallet = (IWallet)walletConstructor.Invoke(["wallet-" + i, (int)anonScoreTarget, liquidityClue, cjSkipFactors]);
    
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

// NOTE: Always creates the file
StreamWriter jsonFile = new("../coinjoins.json", false); 
JsonSerializerOptions serializerOptions = new()
{
    WriteIndented = true,
};
serializerOptions.Converters.Add(new DumbTransactionConverter(wallets));
serializerOptions.Converters.Add(new CoinjoinEnumerableConverter());

long nRounds = scenario.Rounds == 0 ? long.MaxValue : scenario.Rounds; // NOTE: long.MaxValue is basically infinity
for (long i = 0; i < nRounds; i++) 
{
    Console.WriteLine(i);

    CoinjoinResult result = mixer.CompleteMix(wallets);

    List<CoinjoinResult> results = new List<CoinjoinResult>{result};
    
    string coinjoinJson = JsonSerializer.Serialize(results, serializerOptions);
    jsonFile.WriteLine(coinjoinJson);
}

jsonFile.Close();

return 0;