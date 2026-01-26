using NBitcoin;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WabiSabi.Crypto.Randomness;
using Soju.Blockchain.TransactionOutputs;
using Soju.Exceptions;
using Soju.Extensions;
using Soju.Logging;
using Soju.WabiSabi.Backend.Models;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client.StatusChangedEvents;
using Soju.WabiSabi.Models;
using Soju.WabiSabi.Models.MultipartyTransaction;
using WabiSabi.CredentialRequesting;

namespace Soju.WabiSabi.Client;

public class CoinJoinClient
{
	private static readonly Money MinimumOutputAmountSanity = Money.Coins(0.0001m); // ignore rounds with too big minimum denominations
	
	public CoinJoinClient(
		IKeyChain keyChain,
		OutputProvider outputProvider,
		string coordinatorIdentifier,
		CoinJoinCoinSelector coinJoinCoinSelector,
		LiquidityClueProvider liquidityClueProvider,
		TimeSpan feeRateMedianTimeFrame = default)
	{
		KeyChain = keyChain;
		OutputProvider = outputProvider;
		CoordinatorIdentifier = coordinatorIdentifier;
		LiquidityClueProvider = liquidityClueProvider;
		CoinJoinCoinSelector = coinJoinCoinSelector;
		FeeRateMedianTimeFrame = feeRateMedianTimeFrame;
		SecureRandom = new SecureRandom();
	}

	public ImmutableList<SmartCoin> CoinsInCriticalPhase { get; private set; } = ImmutableList<SmartCoin>.Empty;

	private SecureRandom SecureRandom { get; }
	private IKeyChain KeyChain { get; }
	private OutputProvider OutputProvider { get; }
	private string CoordinatorIdentifier { get; }
	private LiquidityClueProvider LiquidityClueProvider { get; }
	private CoinJoinCoinSelector CoinJoinCoinSelector { get; }

	private TimeSpan FeeRateMedianTimeFrame { get; }
	
	// NOTE: My additions
	public Dictionary<Guid, InputRegistrationRequestData> PendingInputRegistrationRequests;
	public Dictionary<Guid, ConnectionConfirmationRequestData> PendingConnectionConfirmationRequests;

	public IEnumerable<SmartCoin> StartCoinJoin(RoundState currentRoundState, IEnumerable<SmartCoin> coinCandidates)
	{
		Debug.Assert(coinCandidates.Any());

		RoundParameters roundParameters = currentRoundState.CoinjoinState.Parameters;

		var liquidityClue = LiquidityClueProvider.GetLiquidityClue(roundParameters.MaxSuggestedAmount);
		var utxoSelectionParameters = UtxoSelectionParameters.FromRoundParameters(roundParameters);

		ImmutableList<SmartCoin> coins = CoinJoinCoinSelector.SelectCoinsForRound(coinCandidates, utxoSelectionParameters, liquidityClue);

		if (!roundParameters.AllowedInputTypes.Contains(ScriptType.P2WPKH) || !roundParameters.AllowedOutputTypes.Contains(ScriptType.P2WPKH))
		{
			currentRoundState.LogInfo($"Skipping the round since it doesn't support P2WPKH inputs and outputs.");

			return ImmutableList<SmartCoin>.Empty;
		}

		if (roundParameters.MaxSuggestedAmount != default && coins.Any(c => c.Amount > roundParameters.MaxSuggestedAmount))
		{
			currentRoundState.LogInfo($"Skipping the round for more optimal mixing. Max suggested amount is '{roundParameters.MaxSuggestedAmount}' BTC, biggest coin amount is: '{coins.Select(c => c.Amount).Max()}' BTC.");

			return ImmutableList<SmartCoin>.Empty;
		}

		if (coins.IsEmpty)
		{
			throw new CoinJoinClientException(CoinjoinError.NoCoinsEligibleToMix, $"No coin was selected from '{coinCandidates.Count()}' number of coins. Probably it was not economical, total amount of coins were: {Money.Satoshis(coinCandidates.Sum(c => c.Amount))} BTC.");
		}

		return coins;
	}

	internal static bool SanityCheck(IEnumerable<TxOut> expectedOutputs, IEnumerable<TxOut> coinJoinOutputs)
	{
		bool AllExpectedScriptsArePresent() =>
			coinJoinOutputs
				.Select(x => x.ScriptPubKey)
				.IsSuperSetOf(expectedOutputs.Select(x => x.ScriptPubKey));

		bool AllOutputsHaveAtLeastTheExpectedValue() =>
			coinJoinOutputs
				.Join(
					expectedOutputs,
					x => x.ScriptPubKey,
					x => x.ScriptPubKey,
					(coinjoinOutput, expectedOutput) => coinjoinOutput.Value - expectedOutput.Value)
				.All(x => x >= Money.Zero);

		return AllExpectedScriptsArePresent() && AllOutputsHaveAtLeastTheExpectedValue();
	}
	
	public List<InputRegistrationRequestWithId> CreateInputRegistrationRequests(IEnumerable<SmartCoin> smartCoins, RoundState roundState) 
	{
		List<InputRegistrationRequestWithId> requests = [];
		
		foreach (SmartCoin coin in smartCoins) 
		{
			try 
			{	
				InputRegistrationRequest request = new (
					roundState.Id,
					coin.Coin.Outpoint);
				
				Guid requestGuid = Guid.NewGuid();
				this.PendingInputRegistrationRequests[requestGuid] = new InputRegistrationRequestData(
					Request: request,
					RoundState: roundState,
					Coin: coin); 
				
				requests.Add(new InputRegistrationRequestWithId(Request: request, Id: requestGuid));
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
			}
		}
		return requests;
	}
	
	public InputRegistrationResponseHandlingResult HandleInputRegistrationResponses(ICollection<InputRegistrationResponseWithId> responses)
	{
		List<AliceClient> alices = [];
		
		foreach ((InputRegistrationResponse response, Guid id) in responses)
		{
			InputRegistrationRequestData requestData = this.PendingInputRegistrationRequests[id];
			
			AliceClient aliceClient = new(response.AliceId, requestData.RoundState, requestData.Coin, response.IsCoordinationFeeExempted);
			requestData.Coin.CoinJoinInProgress = true;
			
			Logger.LogInfo($"Round ({requestData.RoundState.Id}), Alice ({aliceClient.AliceId}): Registered {requestData.Coin.Outpoint}.");
			
			alices.Add(aliceClient);
			this.PendingInputRegistrationRequests.Remove(id);
		}
		
		// NOTE: The successful requests were already removed in the loop
		List<SmartCoin> notRegisteredCoins = this.PendingInputRegistrationRequests.Values.Select(requestData => requestData.Coin).ToList();
		this.PendingInputRegistrationRequests.Clear();
		
		return new InputRegistrationResponseHandlingResult(alices, notRegisteredCoins);
	}
	
	// TTODO: Mixer.v1
	public List<ConnectionConfirmationRequestWithId> CreateConnectionConfirmationRequests(List<AliceClient> alices)
	{
		List<ConnectionConfirmationRequestWithId> requests = [];
		foreach (AliceClient alice in alices)
		{
			long amountToRequest = alice.EffectiveValue.Satoshi;
			long vsizeToRequest = alice.MaxVsizeAllocationPerAlice - alice.SmartCoin.ScriptPubKey.EstimateInputVsize();
			
			uint256 roundId = alice.RoundId;
			Guid aliceId = alice.AliceId;
			
			ConnectionConfirmationRequest request = new (
				RoundId: roundId,
				AliceId: aliceId,
				RealAmountCredentialRequestDelta: amountToRequest,
				RealVsizeCredentialRequestDelta: vsizeToRequest);
			
			Guid requestGuid = Guid.NewGuid();
			this.PendingConnectionConfirmationRequests[requestGuid] = new ConnectionConfirmationRequestData(
				Request: request,
				AliceClient: alice,
				RealAmountCredsRequest: amountToRequest,
				RealVsizeCredsRequest: vsizeToRequest);
			requests.Add(new ConnectionConfirmationRequestWithId(request, requestGuid));
		}
		return requests;
	}
	
	public List<AliceClient> HandleConnectionConfirmationResponses(ICollection<ConnectionConfirmationResponseWithId> responses)
	{
		List<AliceClient> alices = [];
		
		foreach ((ConnectionConfirmationResponse response, Guid id) in responses)
		{
			ConnectionConfirmationRequestData requestData = this.PendingConnectionConfirmationRequests[id];
			
			AliceClient aliceClient = requestData.AliceClient;
			aliceClient.IssuedAmountCredentialsValue = response.RealAmountCredentials;
			aliceClient.IssuedVsizeCredentialsValue = response.RealVsizeCredentials;
			
			alices.Add(aliceClient);
			PendingConnectionConfirmationRequests.Remove(id);
		}
		Debug.Assert(!PendingConnectionConfirmationRequests.Any());
		return alices;
	}
	
	public ImmutableArray<TxOut> CreateOutputs(RoundState roundState, ImmutableArray<AliceClient> registeredAliceClients)
	{
		Debug.Assert(roundState.Phase == Phase.OutputRegistration);
		
		RoundParameters roundParameters = roundState.CoinjoinState.Parameters;
		
		IEnumerable<Coin> registeredCoins = registeredAliceClients.Select(x => x.SmartCoin.Coin);
		
		long availableVsize = registeredAliceClients.Sum(alice => alice.IssuedVsizeCredentialsValue);
		Debug.Assert(availableVsize > 0);
		
		ConstructionState constructionState = roundState.Assert<ConstructionState>();
		
		IEnumerable<Coin> theirCoins = constructionState.Inputs.Where(coin => !registeredCoins.Any(myCoin => coin.Outpoint == myCoin.Outpoint));
		IEnumerable<Money> registeredCoinEffectiveValues = registeredAliceClients.Select(alice => alice.EffectiveValue);
		IEnumerable<Money> theirCoinEffectiveValues = theirCoins.Select(coin => coin.EffectiveValue(roundParameters.MiningFeeRate, roundParameters.CoordinationFeeRate));
		
		ImmutableArray<TxOut> outputTxOuts = OutputProvider.GetOutputs(roundParameters, registeredCoinEffectiveValues, theirCoinEffectiveValues, (int)availableVsize).ToImmutableArray();
		
		return outputTxOuts;
	}
	
	public OutputRegistrationRequest[] CreateOutputRegistrationRequests(RoundState roundState, IList<TxOut> outputs)
	{
		OutputRegistrationRequest[] requests = new OutputRegistrationRequest[outputs.Count];
		
		for (int i = 0; i < outputs.Count; i++) 
		{
			TxOut output = outputs[i];
			FeeRate feeRate = roundState.CoinjoinState.Parameters.MiningFeeRate;
			int outputVsize = output.ScriptPubKey.EstimateOutputVsize();
			// NOTE: Reversing the calculation in Bob.CalculateOutputAmount
			RealCredentialsRequest amountCredentialsRequest = new(-(output.Value + feeRate.GetFee(outputVsize)), [], [], []);
			RealCredentialsRequest vsizeCredentialsRequest = new(-outputVsize, [], [], []);
			requests[i] = new(roundState.Id, output.ScriptPubKey, amountCredentialsRequest, vsizeCredentialsRequest );
		}
		return requests;
	}
	
	public TransactionSignaturesRequest[] SignTransaction(RoundState roundState, ImmutableArray<AliceClient> registeredAliceClients, IEnumerable<TxOut> outputTxOuts)
	{
		SigningState signingState = roundState.Assert<SigningState>();
		TransactionWithPrecomputedData unsignedCoinJoin = signingState.CreateUnsignedTransactionWithPrecomputedData();
		
		// If everything is okay, then sign all the inputs. Otherwise, in case there are missing outputs, the server is
		// lying (it lied us before when it responded with 200 OK to the OutputRegistration requests or it is lying us
		// now when we identify as satoshi.
		// In this scenario we should ban the coordinator and stop dealing with it.
		// see more: https://github.com/zkSNACKs/WalletWasabi/issues/8171
		bool mustSignAllInputs = SanityCheck(outputTxOuts, unsignedCoinJoin.Transaction.Outputs);
		if (!mustSignAllInputs)
		{
			roundState.LogInfo($"There are missing outputs. A subset of inputs will be signed.");
		}
		
		// Send signature.
		ImmutableArray<AliceClient> alicesToSign = mustSignAllInputs
			? registeredAliceClients
			: registeredAliceClients.RemoveAt(SecureRandom.GetInt(0, registeredAliceClients.Length));
		
		TransactionSignaturesRequest[] sigRequests = new TransactionSignaturesRequest[alicesToSign.Length];
		for (int i = 0; i < alicesToSign.Length; i++)
		{
			Coin coin = alicesToSign[i].SmartCoin.Coin;
			var signedCoinJoin = KeyChain.Sign(unsignedCoinJoin.Transaction, coin, unsignedCoinJoin.PrecomputedTransactionData);
			var txInput = signedCoinJoin.Inputs.AsIndexedInputs().First(input => input.PrevOut == coin.Outpoint);
			if (!txInput.VerifyScript(coin, ScriptVerify.Standard, unsignedCoinJoin.PrecomputedTransactionData, out var error))
			{
				throw new InvalidOperationException($"Witness is missing. Reason {nameof(ScriptError)} code: {error}.");
			}
			sigRequests[i] = new TransactionSignaturesRequest(roundState.Id, txInput.Index, txInput.WitScript);
		}
		return sigRequests;
	}

	private void LogCoinJoinSummary(ImmutableArray<AliceClient> registeredAliceClients, IEnumerable<TxOut> myOutputs, RoundState roundState)
	{
		RoundParameters roundParameters = roundState.CoinjoinState.Parameters;
		FeeRate feeRate = roundParameters.MiningFeeRate;

		var totalEffectiveInputAmount = Money.Satoshis(registeredAliceClients.Sum(a => a.EffectiveValue));
		var totalEffectiveOutputAmount = Money.Satoshis(myOutputs.Sum(a => a.Value - feeRate.GetFee(a.ScriptPubKey.EstimateOutputVsize())));
		var effectiveDifference = totalEffectiveInputAmount - totalEffectiveOutputAmount;

		var totalInputAmount = Money.Satoshis(registeredAliceClients.Sum(a => a.SmartCoin.Amount));
		var totalOutputAmount = Money.Satoshis(myOutputs.Sum(a => a.Value));
		var totalDifference = Money.Satoshis(totalInputAmount - totalOutputAmount);

		var inputNetworkFee = Money.Satoshis(registeredAliceClients.Sum(alice => feeRate.GetFee(alice.SmartCoin.Coin.ScriptPubKey.EstimateInputVsize())));
		var outputNetworkFee = Money.Satoshis(myOutputs.Sum(output => feeRate.GetFee(output.ScriptPubKey.EstimateOutputVsize())));
		var totalNetworkFee = inputNetworkFee + outputNetworkFee;
		var totalCoordinationFee = Money.Satoshis(registeredAliceClients.Where(a => !a.IsCoordinationFeeExempted).Sum(a => roundParameters.CoordinationFeeRate.GetFee(a.SmartCoin.Amount)));

		string[] summary = new string[]
		{
			"",
			$"\tInput total : {totalInputAmount.ToString(true, false)} Eff: {totalEffectiveInputAmount.ToString(true, false)} NetworkFee: {inputNetworkFee.ToString(true, false)} CoordFee: {totalCoordinationFee.ToString(true)}",
			$"\tOutput total: {totalOutputAmount.ToString(true, false)} Eff: {totalEffectiveOutputAmount.ToString(true, false)} NetworkFee: {outputNetworkFee.ToString(true, false)}",
			$"\tTotal diff  : {totalDifference.ToString(true, false)}",
			$"\tEffect diff : {effectiveDifference.ToString(true, false)}",
			$"\tTotal fee   : {totalNetworkFee.ToString(true, false)}"
		};

		roundState.LogDebug(string.Join(Environment.NewLine, summary));
	}
}

// NOTE: Record to use when handling input registration response
public record InputRegistrationRequestData(InputRegistrationRequest Request, RoundState RoundState, SmartCoin Coin);
// TTODO: Mixer.v1
public record InputRegistrationRequestWithId(InputRegistrationRequest Request, Guid Id);
public record InputRegistrationResponseWithId(InputRegistrationResponse Response, Guid Id);
public record InputRegistrationResponseHandlingResult(List<AliceClient> AliceClients, List<SmartCoin> NotRegisteredCoins);

// TTODO: Mixer.v1
public record ConnectionConfirmationRequestWithId(ConnectionConfirmationRequest Request, Guid Id);
// NOTE: Record to use when handling connection confirmation response
public record ConnectionConfirmationRequestData(ConnectionConfirmationRequest Request, AliceClient AliceClient, long RealAmountCredsRequest, long RealVsizeCredsRequest);
public record ConnectionConfirmationResponseWithId(ConnectionConfirmationResponse Response, Guid Id);
