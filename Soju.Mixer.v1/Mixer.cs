using NBitcoin;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Soju.BitcoinCore.Rpc;
using Soju.Blockchain.Analysis;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.Transactions;
using Soju.Helpers;
using Soju.WabiSabi.Backend.PostRequests;
using Soju.Extensions;
using Soju.WabiSabi.Backend.Rounds;
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
	public RoundParameters RoundParams { get; }
	private readonly BlockchainAnalyzer _bcAnalyzer;
	public Arena Arena;
	public List<ICoinJoinClientManager> cjManagers;

	public Mixer(RoundParameters roundParams)
	{
		RoundParams = roundParams;
		// TODO: Look more into why BlockchainAnalyzer isn't a static class.
		_bcAnalyzer = new BlockchainAnalyzer();
	}

	public void CompleteMix()
	{
		WasabiRandom wrnd = new InsecureRandom();
		
		Arena.TimeoutRounds();
		
		Dictionary<WalletId, CoinJoinClientContext> cjCtxs = [];
		foreach (var cjManager in cjManagers)
		{
			cjCtxs[cjManager.GetWalletId()] = cjManager.CreateCoinJoinClientCtx();
		}
				
		Arena.CreateRound();
		Arena.SetRoundStates();
		
		Debug.Assert(Arena.RoundStates.Count == 1);
				
		RoundState roundState = Arena.RoundStates[0];
		
		ConcurrentBag<(WalletId WalletId, InputRegistrationRequestWithId Request)> inputRequestBag = [];
		foreach (CoinJoinClientContext cjCtx in cjCtxs.Values) 
		{
			IEnumerable<SmartCoin> coins = cjCtx.StartRoundAndGetCoins(roundState);
			List<InputRegistrationRequestWithId> requests = cjCtx.CoinJoinClient.CreateInputRegistrationRequests(coins, roundState);
			foreach (InputRegistrationRequestWithId request in requests)
			{
				inputRequestBag.Add((cjCtx.WalletId, request));
			}
		}
		(WalletId WalletId, InputRegistrationRequestWithId RequestWithId)[] inputRequestsWithWalletIds = inputRequestBag.ToArray();
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
		
		ConcurrentBag<(WalletId WalletId, ConnectionConfirmationRequestWithId RequestWithId)> ccRequestBag = [];
		
		foreach (CoinJoinClientContext cjCtx in cjCtxs.Values)
		{
			List<ConnectionConfirmationRequestWithId> requests =  cjCtx.CoinJoinClient.CreateConnectionConfirmationRequests(aliceClientsNeedToConfirm[cjCtx.WalletId]);
			foreach (var request in requests)
			{
				ccRequestBag.Add(new (cjCtx.WalletId, request));
			}
		}
		(WalletId WalletId, ConnectionConfirmationRequestWithId RequestWithId)[] ccRequestsWithWalletIds = ccRequestBag.ToArray();
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
			
			(cjCtx.WantedOutputs, cjCtx.Graph) = cjCtx.CoinJoinClient.CreateOutputsAndDependencyGraph(roundState, regAliceClients);
		}
		
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
		
		Arena.StepTransactionSigningPhase();
		Arena.SetRoundStates();
		Debug.Assert(Arena.RoundStates[0].Phase == Phase.TransactionSigning);
	}
}