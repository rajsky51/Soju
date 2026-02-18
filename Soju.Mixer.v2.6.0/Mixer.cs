using NBitcoin;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using Soju.BitcoinRpc;
using Soju.Blockchain.Keys;
using Soju.Blockchain.TransactionOutputs;
using Soju.Extensions;
using Soju.Logging;
using Soju.WabiSabi.Client;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Coordinator;
using Soju.WabiSabi.Coordinator.Models;
using Soju.WabiSabi.Coordinator.Rounds;
using Soju.WabiSabi.Models;
using Soju.Wallets;
using WabiSabi.Crypto.Randomness;

namespace Soju;

public class Mixer
{
	public Arena Arena;
	public CoinJoinClientManager[] CJManagers;
	public WabiSabiConfig ConfigCheck;
	
	public Mixer(
		CoinJoinClientManager[] cjManagers, 
		WabiSabiConfig config, 
		IRPCClient rpc)
	{
		Arena = new Arena(config, rpc, new RoundParameterFactory(config, rpc.Network));
		CJManagers = cjManagers;
		ConfigCheck = config;
	}
	
	public uint256 CompleteMix()
	{
		WasabiRandom wrnd = new InsecureRandom();
		
		Arena.TimeoutRounds();
		
		Dictionary<Wallets.WalletId, CoinJoinClientContext> cjCtxs = [];
		foreach (var cjManager in CJManagers)
		{
			CoinJoinClientContext cjCtx = cjManager.CreateCoinJoinClientCtx();
			cjCtxs[cjCtx.WalletId] = cjCtx;
		}
				
		Arena.CreateRound();
		Arena.SetRoundStates();
		
		Debug.Assert(Arena.RoundStates.Count == 1);
				
		RoundState roundState = Arena.RoundStates[0];
		Debug.Assert(roundState.Phase == Phase.InputRegistration);
		
		ConcurrentBag<(Wallets.WalletId WalletId, List<InputRegistrationRequestWithId> Requests)> inputRequestBag = [];
		
		Stopwatch sw = Stopwatch.StartNew();
		Parallel.ForEach(cjCtxs.Values, cjCtx =>
		{
			try 
			{
				IEnumerable<SmartCoin> coins = cjCtx.StartRoundAndGetCoins(roundState);
				
				List<InputRegistrationRequestWithId> requests = cjCtx.CoinJoinClient.CreateInputRegistrationRequests(coins, roundState);
				
				inputRequestBag.Add((cjCtx.WalletId, requests));
			} 
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
			}
		});
		sw.Stop();
		Console.WriteLine($"Creating input reg requests took {sw.ElapsedMilliseconds} ms");
		
		(Wallets.WalletId WalletId, InputRegistrationRequestWithId RequestWithId)[] inputRequestsWithWalletIds = 
			inputRequestBag
			.SelectMany(pair => pair.Requests.Select(req => (pair.WalletId, req)))
			.ToArray();
		inputRequestsWithWalletIds.Shuffle(wrnd);
		
		Dictionary<Wallets.WalletId, List<InputRegistrationResponseWithId>> inputRegResponses = [];
		foreach(Wallets.WalletId walletId in cjCtxs.Keys)
		{
			inputRegResponses[walletId] = [];
		}
		
		foreach (var requestWithWalletId in inputRequestsWithWalletIds)
		{
			Wallets.WalletId walletId = requestWithWalletId.WalletId;
			InputRegistrationRequestWithId requestWithid = requestWithWalletId.RequestWithId;
			
			try {
				InputRegistrationResponse response = Arena.RegisterInput(requestWithid.Request);
				InputRegistrationResponseWithId responseWithId = new InputRegistrationResponseWithId(response, requestWithid.Id);
				inputRegResponses[walletId].Add(responseWithId);
			}
			catch (WrongPhaseException wpEx)
			{
				// NOTE: This happens when reaching max input count. We'll just not emit any responses
				Debug.Assert(Arena.Rounds.Count == 1);
				Round round = Arena.Rounds.First();
				Debug.Assert(round.InputCount == ConfigCheck.MaxInputCountByRound);
			}
		}
		
		ConcurrentDictionary<Wallets.WalletId, List<AliceClient>> aliceClientsNeedToConfirm = [];
		
		foreach (Wallets.WalletId walletId in inputRegResponses.Keys)
		{
			CoinJoinClientContext cjCtx = cjCtxs[walletId];
			InputRegistrationResponseHandlingResult handlingResult = cjCtx.CoinJoinClient.HandleInputRegistrationResponses(inputRegResponses[walletId]);
			aliceClientsNeedToConfirm[walletId] = handlingResult.AliceClients;
		}
		Arena.StepInputRegistrationPhase();
		Arena.SetRoundStates();
		
		roundState = Arena.RoundStates[0];
		Debug.Assert(roundState.Phase == Phase.ConnectionConfirmation);
		
		ConcurrentBag<(Wallets.WalletId WalletId, List<ConnectionConfirmationRequestWithId> Requests)> ccRequestBag = [];
		
		sw.Restart();
		Parallel.ForEach(cjCtxs.Values, cjCtx =>
		{
			List<ConnectionConfirmationRequestWithId> requests =  cjCtx.CoinJoinClient.CreateConnectionConfirmationRequests(aliceClientsNeedToConfirm[cjCtx.WalletId]);
			
			ccRequestBag.Add((cjCtx.WalletId, requests));
		});
		sw.Stop();
		Console.WriteLine($"Creating connection confirmation requests took {sw.ElapsedMilliseconds} ms");
		
		(Wallets.WalletId WalletId, ConnectionConfirmationRequestWithId RequestWithId)[] ccRequestsWithWalletIds = 
			ccRequestBag
			.SelectMany(pair => pair.Requests.Select(req => (pair.WalletId, req)))
			.ToArray();
		ccRequestsWithWalletIds.Shuffle(wrnd);
				
		Dictionary<WalletId, List<ConnectionConfirmationResponseWithId>> ccResponses = [];
		foreach(WalletId walletId in cjCtxs.Keys)
		{
			ccResponses[walletId] = [];
		}
		
		foreach (var requestWithWalletId in ccRequestsWithWalletIds)
		{
			WalletId walletId = requestWithWalletId.WalletId;
			ConnectionConfirmationRequestWithId requestWithId = requestWithWalletId.RequestWithId;
			
			ConnectionConfirmationResponse response = Arena.ConfirmConnection(requestWithId.Request);
			ConnectionConfirmationResponseWithId responseWithId = new(response, requestWithId.Id);
			ccResponses[walletId].Add(responseWithId);
		}
		
		foreach (WalletId walletId in ccResponses.Keys)
		{
			CoinJoinClientContext cjCtx = cjCtxs[walletId];
			
			List<AliceClient> aliceClients = cjCtx.CoinJoinClient.HandleConnectionConfirmationResponses(ccResponses[walletId]);
			cjCtx.RegisteredAliceClients = aliceClients.ToImmutableArray();
		}
		Arena.StepConnectionConfirmationPhase();
		Arena.SetRoundStates();
		
		roundState = Arena.RoundStates[0];
		Debug.Assert(roundState.Phase == Phase.OutputRegistration);
		
		sw.Restart();
		Parallel.ForEach(cjCtxs.Values, cjCtx =>
		{
			ImmutableArray<AliceClient> regAliceClients = cjCtx.RegisteredAliceClients;
			
			try 
			{
				cjCtx.WantedOutputs = cjCtx.CoinJoinClient.CreateOutputs(roundState, regAliceClients);
			}
			catch (Exception ex)
			{
				cjCtx.WantedOutputs = []; // TODO: Hack
				Logger.LogWarning(ex);
			}
		});
		sw.Stop();
		Console.WriteLine($"Choosing outputs took {sw.ElapsedMilliseconds} ms");
		
		List<OutputRegistrationRequest> outputRegRequests = [];
		// NOTE: Output registration
		sw.Restart();
		foreach (CoinJoinClientContext cjCtx in cjCtxs.Values)
		{
			OutputRegistrationRequest[] requests = cjCtx.CoinJoinClient.CreateOutputRegistrationRequests(roundState, cjCtx.WantedOutputs);
			outputRegRequests.AddRange(requests);
		}
		sw.Stop();
		Console.WriteLine($"Creating output requests took {sw.ElapsedMilliseconds} ms");
		
		outputRegRequests.Shuffle(wrnd);
		
		
		foreach (OutputRegistrationRequest request in outputRegRequests)
		{
			Arena.RegisterOutput(request);
		}
		
		Arena.StepOutputRegistrationPhase();
		Arena.SetRoundStates();
		
		roundState = Arena.RoundStates[0];
		Debug.Assert(roundState.Phase == Phase.TransactionSigning);
		
		ConcurrentBag<TransactionSignaturesRequest> sigRequestBag = []; 
		
		sw.Restart();
		foreach (CoinJoinClientContext cjCtx in cjCtxs.Values)
		{
			ImmutableArray<AliceClient> regAliceClients = cjCtx.RegisteredAliceClients;
			ImmutableArray<TxOut> regOutputs = cjCtx.WantedOutputs;
			
			TransactionSignaturesRequest[] requests = cjCtx.CoinJoinClient.SignTransaction(roundState, regAliceClients, regOutputs);
			
			foreach (var request in requests)
			{
				sigRequestBag.Add(request);
			}
		}
		sw.Stop();
		Console.WriteLine($"Clients signing transation took {sw.ElapsedMilliseconds} ms");
		
		TransactionSignaturesRequest[] sigRequests = sigRequestBag.ToArray();
		
		foreach (var sigRequest in sigRequests)
		{
			Arena.SignTransaction(sigRequest);
		}
		
		uint256 cjTxId = Arena.StepTransactionSigningPhase();
		Arena.SetRoundStates();
		Debug.Assert(Arena.RoundStates[0].Phase == Phase.Ended);
		
		Transaction cjTx = Arena.Rpc.GetRawTransaction(cjTxId);
		int totalRegAlices = cjCtxs.Sum(kvp => kvp.Value.RegisteredAliceClients.Count());
		int totalWantedOutputs = cjCtxs.Sum(kvp => kvp.Value.WantedOutputs.Count());
		Debug.Assert(cjTx.Inputs.Count() == totalRegAlices);
		Debug.Assert(cjTx.Outputs.Count() - totalWantedOutputs <= 1);
		
		foreach (CoinJoinClientContext cjCtx in cjCtxs.Values)
		{
			Wallet wallet = cjCtx.Wallet;
			wallet.BatchedPayments.MovePaymentsToFinished(cjTxId);
			wallet.OutputProvider.DestinationProvider.TrySetScriptStates(KeyState.Used, cjCtx.WantedOutputs.Select(txOut => txOut.ScriptPubKey));
			// NOTE: This moves the rest
			wallet.BatchedPayments.MovePaymentsToPending();
		}
		
		return cjTxId;
	}
}