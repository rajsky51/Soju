using System.Diagnostics;
using NBitcoin;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.Json;
using Soju;
using Soju.BitcoinCore.Rpc;
using Soju.Blockchain.Keys;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.TransactionProcessing;
using Soju.Blockchain.Transactions;
using Soju.Crypto.Randomness;
using Soju.Extensions;
using Soju.Helpers;
using Soju.Json;
using Soju.Models;
using Soju.WabiSabi.Backend;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client;
using Soju.WabiSabi.Client.CoinJoin;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Models;
using Soju.Wallets;

string scenarioFilePath = "test_scenario.json";

ScenarioParser parser = new();
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

Network network = Network.RegTest;
MyRpc rpc = new(network);

WabiSabiConfig wabiSabiConfig = new();
wabiSabiConfig.MinInputCountByRoundMultiplier = 0.08;
wabiSabiConfig.MaxInputCountByRound = 100;
CoinJoinConfiguration cjConfig = new(
	"foo-coordinator", 
	Constants.DefaultMaxCoinJoinMiningFeeRate,
	Constants.AbsoluteMinInputCount,
	false); // NOTE: Not allowing solo coinjoining for now

Dictionary<int, List<(WalletId WalletId, Money Amount)>> fundingCommands = [];
// TODO: Hack
for (int i = 0; i < 100; i++)
{
	fundingCommands[i] = [];
}

Dictionary<WalletId, CoinJoinClientManager> cjManagers = [];
for (int i = 0; i < 20; i++)
{	
	ScenarioWallet scenWallet = scenario.Wallets[i];
	
	string password = $"foo{i}";
	KeyManager keyManager = KeyManager.CreateNew(out Mnemonic _, password, network);
	keyManager.AnonScoreTarget = scenWallet.AnonScoreTarget;
	keyManager.RedCoinIsolation = scenWallet.RedCoinIsolation;
	
	AllTransactionStore txStore = new (":memory:", network);
	TransactionProcessor txProcessor = new (txStore, keyManager, Money.Coins(Constants.DefaultDustThreshold));
	
	Wallet wallet = new(network, txProcessor, password);
	CoinJoinClientManager cjManager = new(wallet, cjConfig);
	cjManagers[wallet.WalletId] = cjManager;
	
	foreach (ScenarioFund fund in scenWallet.Funds)
	{
		var fundList = fundingCommands[fund.DelayRounds];
		fundList.Add((wallet.WalletId, new Money(fund.Satoshis)));
	} 
}

Mixer mixer = new(cjManagers.Values.ToArray(), wabiSabiConfig, rpc);

int rounds = scenario.Rounds > 0 ? scenario.Rounds : Int32.MaxValue;
for (int i = 0; i < rounds; i++)
{
	// TODO: Very hacky
	int height = (i + 1) * 10_000;
	
	List<(WalletId WalletId, Money Amount)> fundList;
	if (fundingCommands.TryGetValue(i, out fundList))
	{
		int j = 0;
		foreach (var fund in fundList)
		{
			j++;
			Wallet wallet = cjManagers[fund.WalletId].Wallet;
			Transaction tx = Transaction.Create(network);
		
			OutPoint nullOutpoint = new();
			TxIn txInput = new(nullOutpoint, Script.Empty);
			// TODO: This calls KeyManager's GetNextCoinJoinKeys, don't necessarily need that
			IDestination destination = wallet.DestinationProvider.GetNextDestinations(1, false).Single();
			TxOut txOutput = new(fund.Amount, destination.ScriptPubKey);
		
			tx.Inputs.Add(txInput);
			tx.Outputs.Add(txOutput);
		
			rpc.SendRawTransaction(tx);
			Debug.Assert(rpc.GetTxOut(tx.GetHash(), 0) is not null);
		
			SmartTransaction smartTx = new(tx, new Height(height + j));
			wallet.TransactionProcessor.Process(smartTx);
		}
	}
	
	uint256 cjTxId = mixer.CompleteMix();
	Transaction cjTx = rpc.GetRawTransaction(cjTxId);
	
	Height cjHeight = new(height + 9_000);
	foreach (Wallet wallet in cjManagers.Values.Select(manager => manager.Wallet))
	{
		SmartTransaction smartTx = new SmartTransaction(cjTx, cjHeight);
		wallet.TransactionProcessor.Process(smartTx);
	}
}

return 0;