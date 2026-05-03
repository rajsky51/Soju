using System.Collections.Immutable;
using System.Diagnostics;
using NBitcoin;
using NBitcoin.RPC;
using Soju.BitcoinCore.Rpc;
using Soju.Blockchain.Analysis;
using Soju.Blockchain.Keys;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.TransactionProcessing;
using Soju.Blockchain.Transactions;
using Soju.Helpers;
using Soju.Models;
using Soju.WabiSabi.Backend;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client;
using Soju.Wallets;

namespace Soju;

public class ScenarioEngine : IScenarioEngine
{
	public readonly Network Network;
	public readonly MyRpcClient Rpc;
	public readonly WabiSabiConfig WabiSabiConfig;
	public Mixer? Mixer;
	
	public bool CanAddWallets;
	public Dictionary<string, CoinJoinClientManager> CJManagers;
	
	public List<(string WalletName, Transaction Transaction)> FundingTxsForUpcomingRound;
	
	public ScenarioEngine(ScenarioBackend backend)
	{
		Network = Network.RegTest;
		Rpc = new MyRpcClient(Network);
		Mixer = null;
		CanAddWallets = true;
		CJManagers = [];
		FundingTxsForUpcomingRound = [];
		
		WabiSabiConfig wabiSabiConfig = new();
		wabiSabiConfig.CoordinatorIdentifier = backend.CoordinatorIdentifier;
		if (backend.MaxInputCountByRound is not null)
			wabiSabiConfig.MaxInputCountByRound = backend.MaxInputCountByRound.Value;
		if (backend.MinInputCountByRoundMultiplier is not null)
			wabiSabiConfig.MinInputCountByRoundMultiplier = backend.MinInputCountByRoundMultiplier.Value;
		if (backend.MinRegistrableAmountSats is not null)
			wabiSabiConfig.MinRegistrableAmount = Money.Satoshis(backend.MinRegistrableAmountSats.Value);
		if (backend.MaxRegistrableAmountSats is not null)
			wabiSabiConfig.MaxRegistrableAmount = Money.Satoshis(backend.MaxRegistrableAmountSats.Value);
		
		WabiSabiConfig = wabiSabiConfig;
	}
	
	public void CreateAndAddWallet(string name, decimal anonScoreTarget, bool redCoinIsolation)
	{
		Debug.Assert(CanAddWallets);
		
		string password = name;
		
		KeyManager keyManager = KeyManager.CreateNew(out Mnemonic _, password, Network, name);
		keyManager.AnonScoreTarget = (int)anonScoreTarget;
		keyManager.RedCoinIsolation = redCoinIsolation;
		
		AllTransactionStore txStore = new(":memory:", Network);
		TransactionProcessor txProcessor = new(txStore, keyManager, Money.Coins(Constants.DefaultDustThreshold));
		
		Wallet wallet = new(Network, txProcessor, password);
		
		CoinJoinConfiguration cjConfig = new(
			WabiSabiConfig.CoordinatorIdentifier,
			Constants.DefaultMaxCoinJoinMiningFeeRate,
			Constants.AbsoluteMinInputCount,
			AllowSoloCoinjoining: false);
		
		Debug.Assert(!CJManagers.Keys.Contains(name));
		CJManagers[name] = new(wallet, cjConfig);
	}
	
	public void AddWalletFund(string walletName, long satoshis)
	{
		Wallet wallet = CJManagers[walletName].Wallet;
		
		TxIn txInput = new(new OutPoint(), Script.Empty);
		
		IDestination destination = wallet.GetNextReceiveAddress([$"fund of {satoshis} sats to {walletName}"], ScriptPubKeyType.Segwit).GetP2wpkhAddress(Network);
		TxOut txOutput = new(new Money(satoshis), destination);
		
		Transaction tx = Transaction.Create(Network);
		tx.Inputs.Add(txInput);
		tx.Outputs.Add(txOutput);
		tx.PrecomputeHash(true, false);
		
		FundingTxsForUpcomingRound.Add((walletName, tx));
	}
	
	public MixingResult MixRound(decimal miningFeeRateSatoshisPerByte)
	{
		if (Mixer is null) { 
			Mixer = new Mixer(CJManagers.Values.ToArray(), WabiSabiConfig, Rpc);
			CanAddWallets = false;
		}
		
		Rpc.SetCurrentMiningFeeRate(new FeeRate(miningFeeRateSatoshisPerByte));
		
		foreach ((string name, Transaction tx) in FundingTxsForUpcomingRound)
		{
			Rpc.SendRawTransaction(tx);
			Debug.Assert(Rpc.GetTxOut(tx.GetHash(), 0) is not null);
			Height txHeight = Rpc.GetTransactionBlockHeight(tx.GetHash());
			
			SmartTransaction smartTx = new(tx, new Height(txHeight));
			CJManagers[name].Wallet.TransactionProcessor.Process(smartTx);
		}
		FundingTxsForUpcomingRound.Clear();
		
		// NOTE: This is done so the clients' coins have large enough confirmations
		Rpc.BumpHeight(1000); // TODO: Major hack
		
		uint256 cjTxId = Mixer.CompleteMix();
		Transaction cjTx = Rpc.GetRawTransaction(cjTxId);
		int cjTxHeight = Rpc.GetTransactionBlockHeight(cjTxId);
		cjTx.PrecomputeHash(true, false);
		
		Dictionary<string, (string WalletName, double AnonScore)> txInputWalletInfos = [];
		Dictionary<string, (string WalletName, double AnonScore)> txOutputWalletInfos = [];
		foreach (Wallet wallet in CJManagers.Values.Select(manager => manager.Wallet))
		{
			SmartTransaction smartTx = new SmartTransaction(cjTx, new(cjTxHeight));
			wallet.TransactionProcessor.Process(smartTx);
			
			foreach (SmartCoin input in smartTx.WalletInputs) 
			{
				string address = input.Coin.ScriptPubKey.GetDestinationAddress(Network)!.ToString();
				txInputWalletInfos[address] = (wallet.WalletName, input.AnonymitySet);
			}
			foreach (SmartCoin output in smartTx.WalletOutputs) 
			{
				string address = output.Coin.ScriptPubKey.GetDestinationAddress(Network)!.ToString();
				txOutputWalletInfos[address] = (wallet.WalletName, output.AnonymitySet);
			}
		}
		
		MixingInput[] mixingInputs = new MixingInput[cjTx.Inputs.Count];
		for (int i = 0; i < cjTx.Inputs.Count; i++)
		{
			TxIn input = cjTx.Inputs[i];
			GetTxOutResponse response = Rpc.GetTxOut(input.PrevOut.Hash, (int)input.PrevOut.N)!;
			
			TxOut prevTxOut = response.TxOut;
			
			string address = prevTxOut.ScriptPubKey.GetDestinationAddress(Network)!.ToString();
			(string walletName, double anonScore) = txInputWalletInfos[address];
			
			mixingInputs[i] = new MixingInput(
				Address:    address,
				TxId:       input.PrevOut.Hash.ToString(),
				Value:      prevTxOut.Value,
				WalletName: walletName,
				AnonScore:  anonScore);
		}
		
		MixingOutput[] mixingOutputs = new MixingOutput[cjTx.Outputs.Count];
		for (int i = 0; i < cjTx.Outputs.Count; i++)
		{
			TxOut output = cjTx.Outputs[i];
			
			string address = output.ScriptPubKey.GetDestinationAddress(Network)!.ToString();
			(string WalletName, double AnonScore) info;
			if (!txOutputWalletInfos.TryGetValue(address, out info)) 
			{
				info.WalletName = WabiSabiConfig.CoordinatorIdentifier;
				info.AnonScore = 1.0;
			}
			
			mixingOutputs[i] = new MixingOutput(
				Address:    address, 
				Value:      output.Value,
				IsStdDenom: BlockchainAnalyzer.StdDenoms.Contains(output.Value),
				WalletName: info.WalletName,
				AnonScore:  info.AnonScore);
		}
		
		Debug.Assert(Mixer.Arena.RoundStates.Count == 1);
		Debug.Assert(Mixer.Arena.RoundStates[0].Phase == Phase.Ended);
		return new MixingResult(
			Inputs:  mixingInputs,
			Outputs: mixingOutputs,
			TxId:    cjTxId.ToString(),
			RoundId: Mixer.Arena.RoundStates[0].Id.ToString(),
			TransactionVirtualSize: cjTx.GetVirtualSize());
	}
}