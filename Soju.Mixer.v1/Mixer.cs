using NBitcoin;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Soju.BitcoinCore.Rpc;
using Soju.Blockchain.Analysis;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.Transactions;
using Soju.Helpers;
using Soju.WabiSabi.Backend.PostRequests;
using Soju.Extensions;
using Soju.Logging;
using Soju.WabiSabi.Backend;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Backend.Statistics;
using Soju.WabiSabi.Client;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Client.CoinJoin.Client.Decomposer;
using Soju.WabiSabi.Client.CredentialDependencies;
using Soju.WabiSabi.Models;
using Soju.Wallets;
using WabiSabi.Crypto.Randomness;

namespace Soju;

public class  Mixer : IMixer
{
	public Arena Arena;
	public CoinJoinClientManager[] CJManagers;

	public Mixer(
		CoinJoinClientManager[] cjManagers, 
		WabiSabiConfig config, 
		IRPCClient rpc
		)
	{
		Arena = new Arena(config, rpc, new RoundParameterFactory(config, rpc.Network));
		CJManagers = cjManagers;
	}

	public uint256 CompleteMix()
	{
		WasabiRandom wrnd = new InsecureRandom();
		
		Arena.TimeoutRounds();
		
		Dictionary<WalletId, CoinJoinClientContext> cjCtxs = [];
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
		
		ConcurrentBag<(WalletId WalletId, List<InputRegistrationRequestWithId> Requests)> inputRequestBag = [];
		
		long coinSelectionSumMs = 0;
		long inputReqCreationSumMs = 0;
		Parallel.ForEach(cjCtxs.Values, cjCtx =>
		{
			try 
			{
				Stopwatch sw = Stopwatch.StartNew();
				IEnumerable<SmartCoin> coins = cjCtx.StartRoundAndGetCoins(roundState);
				sw.Stop();
				Interlocked.Add(ref coinSelectionSumMs, sw.ElapsedMilliseconds);
				
				sw.Restart();
				List<InputRegistrationRequestWithId> requests = cjCtx.CoinJoinClient.CreateInputRegistrationRequests(coins, roundState);
				sw.Stop();
				Interlocked.Add(ref inputReqCreationSumMs, sw.ElapsedMilliseconds);
				
				inputRequestBag.Add((cjCtx.WalletId, requests));
			} 
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
			}
			
		});
		Console.WriteLine($"Choosing inputs coins took {coinSelectionSumMs} ms");
		Console.WriteLine($"Then creating input reg requests took {inputReqCreationSumMs} ms");
		
		(WalletId WalletId, InputRegistrationRequestWithId RequestWithId)[] inputRequestsWithWalletIds = 
			inputRequestBag
			.SelectMany(pair => pair.Requests.Select(req => (pair.WalletId, req)))
			.ToArray();
		inputRequestsWithWalletIds.Shuffle(wrnd);
		
		Dictionary<WalletId, List<InputRegistrationResponseWithId>> inputRegResponses = [];
		foreach(WalletId walletId in cjCtxs.Keys)
		{
			inputRegResponses[walletId] = [];
		}
		
		foreach (var requestWithWalletId in inputRequestsWithWalletIds)
		{
			WalletId walletId = requestWithWalletId.WalletId;
			InputRegistrationRequestWithId requestWithid = requestWithWalletId.RequestWithId;
			
			InputRegistrationResponse response = Arena.RegisterInput(requestWithid.Request);
			InputRegistrationResponseWithId responseWithId = new InputRegistrationResponseWithId(response, requestWithid.Id);
			inputRegResponses[walletId].Add(responseWithId);
		}
		
		ConcurrentDictionary<WalletId, List<AliceClient>> aliceClientsNeedToConfirm = [];
		
		foreach (WalletId walletId in inputRegResponses.Keys)
		{
			CoinJoinClientContext cjCtx = cjCtxs[walletId];
			InputRegistrationResponseHandlingResult handlingResult = cjCtx.CoinJoinClient.HandleInputRegistrationResponses(inputRegResponses[walletId]);
			aliceClientsNeedToConfirm[walletId] = handlingResult.AliceClients;
		}
		Arena.StepInputRegistrationPhase();
		Arena.SetRoundStates();
		
		roundState = Arena.RoundStates[0];
		Debug.Assert(roundState.Phase == Phase.ConnectionConfirmation);
		
		ConcurrentBag<(WalletId WalletId, List<ConnectionConfirmationRequestWithId> Requests)> ccRequestBag = [];
		
		long ccReqCreationSumMs = 0;
		Parallel.ForEach(cjCtxs.Values, cjCtx =>
		{
			Stopwatch sw = Stopwatch.StartNew();
			List<ConnectionConfirmationRequestWithId> requests =  cjCtx.CoinJoinClient.CreateConnectionConfirmationRequests(aliceClientsNeedToConfirm[cjCtx.WalletId]);
			sw.Stop();
			Interlocked.Add(ref ccReqCreationSumMs, sw.ElapsedMilliseconds);
			
			ccRequestBag.Add((cjCtx.WalletId, requests));
		});
		Console.WriteLine($"Creating connection confirmation requests took {ccReqCreationSumMs} ms");
		
		(WalletId WalletId, ConnectionConfirmationRequestWithId RequestWithId)[] ccRequestsWithWalletIds = 
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
		
		foreach (CoinJoinClientContext cjCtx in cjCtxs.Values)
		{
			ImmutableArray<AliceClient> regAliceClients = cjCtx.RegisteredAliceClients;
			
			try 
			{
				(cjCtx.WantedOutputs, cjCtx.Graph) = cjCtx.CoinJoinClient.CreateOutputsAndDependencyGraph(roundState, regAliceClients);
			}
			catch (Exception ex)
			{
				cjCtx.WantedOutputs = []; // TODO: Hack
				Logger.LogWarning(ex);
			}
		}
		
		// TODO: Resolve dependency graph
		
		List<OutputRegistrationRequest> outputRegRequests = [];
		// NOTE: Output registration
		foreach (CoinJoinClientContext cjCtx in cjCtxs.Values)
		{
			OutputRegistrationRequest[] requests = cjCtx.CoinJoinClient.CreateOutputRegistrationRequests(roundState, cjCtx.WantedOutputs);
			outputRegRequests.AddRange(requests);
		}
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
		
		TransactionSignaturesRequest[] sigRequests = sigRequestBag.ToArray();
		
		foreach (var sigRequest in sigRequests)
		{
			Arena.SignTransaction(sigRequest);
		}
		
		uint256 cjTxId = Arena.StepTransactionSigningPhase();
		Arena.SetRoundStates();
		Debug.Assert(Arena.RoundStates[0].Phase == Phase.Ended);
		
		Debug.Assert(Arena.Rpc.GetRawTransaction(cjTxId) is not null);
		
		return cjTxId;
	}
}