using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Soju;
using Soju.Blockchain.Keys;

Console.WriteLine($"CWD: {Directory.GetCurrentDirectory()}");

string scenarioFilePath = "/home/talar/projects/soju/Soju/test_scenario.json";

int defaultAnonScoreTarget = 5;
bool defaultRedCoinIsolation = false;

decimal defaultMiningFee = 150m;

ScenarioParser parser = new(defaultAnonScoreTarget, defaultRedCoinIsolation);
ScenarioConfig? scenario = parser.Parse(scenarioFilePath);
foreach (string error in parser.Errors)
{
	Console.WriteLine($"[ERROR] {error}");
}
foreach (string warning in parser.Warnings)
{
	Console.WriteLine($"[WARNING] {warning}");
}
if (scenario is null || parser.Errors.Count > 0)
{
	return 1;
}

string assemblyPath = "/home/talar/projects/soju/Soju.Mixer.v1/bin/Debug/net9.0/Soju.Mixer.v1.dll";
Assembly assembly = Assembly.LoadFrom(assemblyPath);

Type engineType = assembly.GetTypes()
	.First(t => typeof(IScenarioEngine).IsAssignableFrom(t)
	            && !t.IsInterface
	            && !t.IsAbstract);

IScenarioEngine engine = (IScenarioEngine)Activator.CreateInstance(engineType, [scenario.Backend])!;

Stopwatch sw = Stopwatch.StartNew();
for (int i = 0; i < scenario.Wallets.Count; i++)
{
	ScenarioWallet scenWallet = scenario.Wallets[i];
	
	engine.CreateAndAddWallet($"wallet-{i}", scenWallet.AnonScoreTarget, scenWallet.RedCoinIsolation);
	Console.WriteLine($"Created wallet-{i}");
}
sw.Stop();
Console.WriteLine($"Generating wallets took {sw.Elapsed}");
Console.WriteLine($"That's {sw.Elapsed / scenario.Wallets.Count} per wallet");

Dictionary<int, List<(string WalletName, long AmountSats)>> fundingCommands = [];
for (int i = 0; i < scenario.Wallets.Count; i++)
{
	ScenarioWallet scenWallet = scenario.Wallets[i];
	string walletName = $"wallet-{i}";
	foreach (ScenarioFund fund in scenWallet.Funds)
	{
		List<(string WalletName, long AmountSats)> fundList;
		if (!fundingCommands.TryGetValue(fund.DelayRounds, out fundList))
		{
			fundList = [];
			fundingCommands[fund.DelayRounds] = fundList;
		}
		fundList.Add((walletName, fund.Satoshis));
	}
}

List<(int DelayRounds, decimal SatsPerByte)> miningFeeChanges = scenario.MiningFees.Select(fee => (fee.DelayRounds, fee.SatoshisPerByte)).OrderBy(fee => fee.DelayRounds).ToList();
if (!miningFeeChanges.Any() || miningFeeChanges[0].DelayRounds > 0)
	miningFeeChanges.Insert(0, (0, defaultMiningFee));

List<MixingResult> mixes = [];
int totalRounds = scenario.Rounds > 0 ? scenario.Rounds : Int32.MaxValue;
for (int i = 0; i < totalRounds; i++)
{
	if (fundingCommands.TryGetValue(i, out var fundList))
	{
		foreach (var fund in fundList)
			engine.AddWalletFund(fund.WalletName, fund.AmountSats);
	}
	
	decimal miningFeeRate = 0m;
	foreach (var miningFeeChange in miningFeeChanges)
	{
		if (miningFeeChange.DelayRounds <= i)
			miningFeeRate = miningFeeChange.SatsPerByte;
	}
	
	MixingResult mixResult = engine.MixRound(miningFeeRate);
	mixes.Add(mixResult);
	
	long inputSumSats = mixResult.Inputs.Sum(input => input.Value);
	long outputSumSats = mixResult.Outputs.Sum(output => output.Value);
	long miningFeeSumSats = inputSumSats - outputSumSats;
	
	double inputTotalAnonScore = mixResult.Inputs.Sum(input => input.AnonScore);
	double outputTotalAnonScore = mixResult.Outputs.Sum(output => output.AnonScore);
	double anonScoreIncrease = outputTotalAnonScore - inputTotalAnonScore;
	
	// NOTE: Try to find the coordinator output
	MixingOutput coordinatorOutput = new();
	foreach (var output in mixResult.Outputs)
	{
		if (output.WalletName == scenario.Backend.CoordinatorIdentifier)
		{
			Debug.Assert(coordinatorOutput.Value == 0);
			coordinatorOutput = output;
		}
	}
	
	// TODO: Decide on what to do with the coordinator output
	Console.WriteLine($"Round {i} ended");
	Console.WriteLine($"Input count: {mixResult.Inputs.Length}");
	Console.WriteLine($"Output count: {mixResult.Outputs.Length}");
	Console.WriteLine($"Inputs sum: {(decimal)inputSumSats / 100_000_000} BTC");
	Console.WriteLine($"Outputs sum: {(decimal)outputSumSats / 100_000_000} BTC");
	Console.WriteLine($"Total anonymity score increase: {anonScoreIncrease}");
	Console.WriteLine($"Anonymity score increase per output: {anonScoreIncrease / mixResult.Outputs.Length}");
	Console.WriteLine($"Mining fee: {miningFeeSumSats} sats, that's {(decimal)miningFeeSumSats / mixResult.TransactionVirtualSize} sats/vb");
	Console.WriteLine($"Coordinator output: {coordinatorOutput.Value}");
	Console.WriteLine();
}

var root = new Dictionary<string, object>();
var coinjoins = new Dictionary<string, object>();

for (int ix = 0; ix < mixes.Count; ix++)
{
	MixingResult cj = mixes[ix];
	var coinjoinObj = new Dictionary<string, object>();

	coinjoinObj["txid"] = cj.TxId;
	coinjoinObj["relative_order"] = ix;

	// ----- INPUTS -----
	var inputsObj = new Dictionary<string, object>();

	for (int i = 0; i < cj.Inputs.Length; i++)
	{
		var input = cj.Inputs[i];

		var inputObj = new Dictionary<string, object>();
		inputObj["address"] = input.Address;
		inputObj["txid"] = input.TxId;
		inputObj["value"] = input.Value;
		inputObj["wallet_name"] = input.WalletName;

		inputsObj[i.ToString()] = inputObj;
	}

	coinjoinObj["inputs"] = inputsObj;

	// ----- OUTPUTS -----
	var outputsObj = new Dictionary<string, object>();

	for (int i = 0; i < cj.Outputs.Length; i++)
	{
		var output = cj.Outputs[i];

		var outputObj = new Dictionary<string, object>();
		outputObj["address"] = output.Address;
		outputObj["value"] = output.Value;
		outputObj["wallet_name"] = output.WalletName;

		outputsObj[i.ToString()] = outputObj;
	}

	coinjoinObj["outputs"] = outputsObj;

	coinjoins[cj.TxId] = coinjoinObj;
}

root["coinjoins"] = coinjoins;

// Serialize
var options = new JsonSerializerOptions
{
	WriteIndented = true
};

string json = JsonSerializer.Serialize(root, options);
string outputFilePath = Path.Combine(
	Path.GetDirectoryName(scenarioFilePath)!,
	Path.GetFileNameWithoutExtension(scenarioFilePath) + "_output.json");

File.WriteAllText(outputFilePath, json);

return 0;