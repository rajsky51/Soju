using System.Collections.Concurrent;
using System.Diagnostics;
using NBitcoin;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.Json;
using NBitcoin.RPC;
using Soju;
using Soju.BitcoinCore.Rpc;
using Soju.Blockchain.Analysis;
using Soju.Blockchain.Keys;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.TransactionProcessing;
using Soju.Blockchain.Transactions;
using Soju.Crypto.Randomness;
using Soju.Extensions;
using Soju.Helpers;
using Soju.Models;
using Soju.WabiSabi.Backend;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client;
using Soju.WabiSabi.Client.Batching;
using Soju.WabiSabi.Client.CoinJoin;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Models;
using Soju.Wallets;

decimal defaultMiningFee = 2m;
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
ScenarioBackend backend = scenario.Backend;
if (backend.CoordinatorIdentifier is not null) 
	wabiSabiConfig.CoordinatorIdentifier = backend.CoordinatorIdentifier;
if (backend.MaxInputCountByRound is not null)
	wabiSabiConfig.MaxInputCountByRound = backend.MaxInputCountByRound.Value;
if (backend.MinInputCountByRoundMultiplier is not null)
	wabiSabiConfig.MinInputCountByRoundMultiplier = backend.MinInputCountByRoundMultiplier.Value;
if (backend.MinRegistrableAmount is not null)
	wabiSabiConfig.MinRegistrableAmount = backend.MinRegistrableAmount;
if (backend.MaxRegistrableAmount is not null)
	wabiSabiConfig.MaxRegistrableAmount = backend.MaxRegistrableAmount;

CoinJoinConfiguration cjConfig = new(
	wabiSabiConfig.CoordinatorIdentifier, 
	Constants.DefaultMaxCoinJoinMiningFeeRate,
	Constants.AbsoluteMinInputCount,
	false); // NOTE: Not allowing solo coinjoining

Dictionary<WalletId, CoinJoinClientManager> cjManagers = [];

Wallet[] generatedWallets = new Wallet[scenario.Wallets.Count];

long walletCreationCount = 0;

Stopwatch sw = Stopwatch.StartNew();
for (int i = 0; i < scenario.Wallets.Count; i++)
{
	ScenarioWallet scenWallet = scenario.Wallets[i];
	
	string password = $"foo{i}";
	string walletName = $"wallet-{i}";
	
	Console.WriteLine($"Start '{walletName}'");
	
	Console.WriteLine($"KM start '{walletName}'");	
	KeyManager keyManager = KeyManager.CreateNew(out Mnemonic _, password, network, walletName);
	keyManager.AnonScoreTarget = scenWallet.AnonScoreTarget;
	keyManager.RedCoinIsolation = scenWallet.RedCoinIsolation;
	Console.WriteLine($"KM finish '{walletName}'");

	AllTransactionStore txStore = new(":memory:", network);
	TransactionProcessor txProcessor = new(txStore, keyManager, Money.Coins(Constants.DefaultDustThreshold));
	
	Wallet wallet = new(network, txProcessor, password);
	
	generatedWallets[i] = wallet;
	Interlocked.Increment(ref walletCreationCount);
	Console.WriteLine($"Created '{walletName}', total: {walletCreationCount}");
}
sw.Stop();
Console.WriteLine($"Creating {scenario.Wallets.Count} wallets took {(float)sw.ElapsedMilliseconds / 1000} s.\nThat's {(float)sw.ElapsedMilliseconds / 1000 / scenario.Wallets.Count} s per wallet");

Dictionary<int, List<(WalletId WalletId, Money Amount)>> fundingCommands = [];
for (int i = 0; i < generatedWallets.Count(); i++)
{
	Wallet wallet = generatedWallets[i];
	ScenarioWallet scenWallet = scenario.Wallets[i];
	
	CoinJoinClientManager cjManager = new(wallet, cjConfig);
	cjManagers[wallet.WalletId] = cjManager;
	
	foreach (ScenarioFund fund in scenWallet.Funds)
	{
		List<(WalletId WalletId, Money Amount)> fundList;
		if (!fundingCommands.TryGetValue(fund.DelayRounds, out fundList)) 
		{
			fundList = [];
			fundingCommands[fund.DelayRounds] = fundList;
		}
		fundList.Add((wallet.WalletId, new Money(fund.Satoshis)));
	} 
}

// TODO: Cannot pay out of the coinjoin
Dictionary<int, List<(Money Amount, WalletId SenderWalletId, BitcoinAddress ReceiveAddress)>> paymentCommands = [];
foreach (ScenarioPayment scenPayment in scenario.Payments)
{
	Wallet senderWallet = generatedWallets[scenPayment.SenderWalletIx];
	Wallet receiverWallet = generatedWallets[scenPayment.ReceiverWalletIx];
	
	HdPubKey pubkey = receiverWallet.KeyManager.GetNextReceiveKey($"payment {scenPayment.Satoshis} {scenPayment.SenderWalletIx} -> {scenPayment.ReceiverWalletIx}");
	BitcoinAddress receiveAddress = pubkey.GetAddress(network);
	
	if (!paymentCommands.TryGetValue(scenPayment.DelayRounds, out var paymentList))
	{
		paymentList = [];
		paymentCommands[scenPayment.DelayRounds] = paymentList;
	}
	paymentList.Add((new Money(scenPayment.Satoshis), senderWallet.WalletId, receiveAddress));
}

List<(int DelayRounds, decimal SatoshisPerByte)> miningFeeChanges = scenario.MiningFees.Select(fee => (fee.DelayRounds, fee.SatoshisPerByte)).OrderBy(fee => fee.DelayRounds).ToList();
if (!miningFeeChanges.Any() || miningFeeChanges[0].DelayRounds > 0)
	miningFeeChanges.Insert(0, (0, defaultMiningFee));

Mixer mixer = new(cjManagers.Values.ToArray(), wabiSabiConfig, rpc);

int rounds = scenario.Rounds > 0 ? scenario.Rounds : Int32.MaxValue;
for (int i = 0; i < rounds; i++)
{
	// TODO: Very hacky
	int height = (i + 1) * 10_000;
	
	if (fundingCommands.TryGetValue(i, out var fundList))
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
	
	if (paymentCommands.TryGetValue(i, out var paymentList))
	{
		foreach (var payment in paymentList)
		{
			Wallet senderWallet = cjManagers[payment.SenderWalletId].Wallet;
			senderWallet.AddCoinJoinPayment(payment.ReceiveAddress, payment.Amount);
		}
	}
	
	decimal roundMiningFee = 0m;
	foreach (var miningFeeChange in miningFeeChanges)
	{
		if (miningFeeChange.DelayRounds <= i)
			roundMiningFee = miningFeeChange.SatoshisPerByte;
	}
	rpc.SetCurrentMiningFeeRate(new FeeRate(roundMiningFee));
	
	uint256 cjTxId = mixer.CompleteMix();
	Transaction cjTx = rpc.GetRawTransaction(cjTxId);
	
	OrderedDictionary<string, MixingInput> mixingInputs = [];
	OrderedDictionary<string, MixingOutput> mixingOutputs = [];
	
	long walletsInputSumSats = 0;
	foreach (TxIn input in cjTx.Inputs)
	{
		GetTxOutResponse response = rpc.GetTxOut(input.PrevOut.Hash, (int)input.PrevOut.N)!;
		
		TxOut prevTxOut = response.TxOut;
		walletsInputSumSats += prevTxOut.Value.Satoshi;
		
		BitcoinAddress address = prevTxOut.ScriptPubKey.GetDestinationAddress(network)!;
		
		MixingInput mixInput = new();
		mixInput.Address = address.ToString();
		mixInput.TxId    = input.PrevOut.Hash;
		mixInput.Value   = prevTxOut.Value;
		mixingInputs.Add(mixInput.Address, mixInput);
	}
	
	long walletsOutputSumSats = 0;
	foreach (TxOut output in cjTx.Outputs)
	{
		walletsOutputSumSats += output.Value.Satoshi;
		
		BitcoinAddress address = output.ScriptPubKey.GetDestinationAddress(network)!;
		
		MixingOutput mixOutput = new();
		mixOutput.Address    = address.ToString();
		mixOutput.Value      = output.Value;
		mixOutput.IsStdDenom = BlockchainAnalyzer.StdDenoms.Contains(output.Value);
		mixingOutputs.Add(mixOutput.Address, mixOutput);
	}
	
	int totalWalletInputCount = 0;
	int totalWalletOutputCount = 0;
	foreach (Wallet wallet in cjManagers.Values.Select(manager => manager.Wallet))
	{
		SmartTransaction smartTx = new SmartTransaction(cjTx, new(height + 9_000));
		wallet.TransactionProcessor.Process(smartTx);
		
		foreach (SmartCoin input in smartTx.WalletInputs) 
		{
			BitcoinAddress address = input.Coin.ScriptPubKey.GetDestinationAddress(network)!;
			
			MixingInput mixInput = mixingInputs[address.ToString()];
			mixInput.WalletName = wallet.WalletName;
			mixInput.AnonScore  = input.AnonymitySet;
		}
		
		foreach (SmartCoin output in smartTx.WalletOutputs)
		{
			BitcoinAddress address = output.Coin.ScriptPubKey.GetDestinationAddress(network)!;
			
			MixingOutput mixOutput = mixingOutputs[address.ToString()];
			mixOutput.WalletName = wallet.WalletName;
			mixOutput.AnonScore = output.AnonymitySet;
		}
		
		totalWalletInputCount += smartTx.WalletInputs.Count;
		totalWalletOutputCount += smartTx.WalletOutputs.Count;
	}
	Debug.Assert(totalWalletInputCount == cjTx.Inputs.Count 
	             && (cjTx.Outputs.Count - totalWalletOutputCount) <= 1);
	
	long coordinationFeeSats = 0;
	double coordinatorAnonScore = 0;
	// NOTE: Let's try find coordinator's output
	foreach(MixingOutput output in mixingOutputs.Values)
	{
		if (output.WalletName is null)
		{
			output.WalletName = wabiSabiConfig.CoordinatorIdentifier;
			output.AnonScore = 1.0;
			coordinatorAnonScore = 1.0;
			
			coordinationFeeSats = output.Value;
		}
	}
	// TODO: walletOutputSumSats includes coordinator's output
	long miningFeeSats = walletsInputSumSats - walletsOutputSumSats;
		
	Debug.Assert(mixer.Arena.RoundStates.Count == 1);
	Debug.Assert(mixer.Arena.RoundStates[0].Phase == Phase.Ended);
	uint256 roundId = mixer.Arena.RoundStates[0].Id;
	
	MixingResult mixingResult = new(
		Inputs:        mixingInputs.Values.ToArray(),
		Outputs:       mixingOutputs.Values.ToArray(),
		TxId:          cjTxId,
		RoundId:       roundId,
		RelativeOrder: i);
		
	double inputTotalAnonScore = mixingResult.Inputs.Sum(input => input.AnonScore);
	double outputTotalAnonScore = mixingResult.Outputs.Sum(output => output.AnonScore) - coordinatorAnonScore;
	double anonScoreIncrease = outputTotalAnonScore - inputTotalAnonScore;
	
	Console.WriteLine($"Round {i} ended");
	Console.WriteLine($"Wallet inputs: {totalWalletInputCount}");
	Console.WriteLine($"Wallet outputs: {totalWalletOutputCount}");
	Console.WriteLine($"Wallet inputs sum: {new Money(walletsInputSumSats)}");
	Console.WriteLine($"Wallet outputs sum: {new Money(walletsOutputSumSats)}");
	Console.WriteLine($"Total anonymity score increase: {anonScoreIncrease}");
	Console.WriteLine($"Anonymity score increase per output: {anonScoreIncrease / totalWalletOutputCount}");
	Console.WriteLine($"Mining fee: {miningFeeSats} sats, that's {(decimal)miningFeeSats / cjTx.GetVirtualSize()} sats/vb");
	Console.WriteLine($"Coordination fee: {new Money(coordinationFeeSats)}");
	Console.WriteLine();
}

return 0;