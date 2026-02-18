using NBitcoin;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WabiSabi.Crypto.Randomness;
using Soju.Blockchain.Keys;
using Soju.Blockchain.TransactionOutputs;
using Soju.Extensions;
using Soju.Helpers;
using Soju.Logging;
using Soju.Models;
using Soju.WabiSabi.Client.StatusChangedEvents;
using Soju.WabiSabi.Coordinator.Models;
using Soju.WabiSabi.Coordinator.PostRequests;
using Soju.WabiSabi.Coordinator.Rounds;
using Soju.WabiSabi.Models;
using Soju.WabiSabi.Models.MultipartyTransaction;
using WabiSabi.CredentialRequesting;

namespace Soju.WabiSabi.Client.CoinJoin.Client;

public class CoinJoinClient
{
	private static readonly Money MinimumOutputAmountSanity = Money.Coins(0.0001m); // ignore rounds with too big minimum denominations
	private static readonly TimeSpan ExtraPhaseTimeoutMargin = TimeSpan.FromMinutes(2);
	private static readonly TimeSpan ExtraRoundTimeoutMargin = TimeSpan.FromMinutes(10);

	// Maximum delay when spreading the requests in time, except input registration requests which
	// timings only depends on the input-reg timeout and signing requests which timings must be larger.
	// This is a maximum cap the delay can be smaller if the remaining time is less.
	private static readonly TimeSpan MaximumRequestDelay = TimeSpan.FromSeconds(10);

	public CoinJoinClient(
		IKeyChain keyChain,
		OutputProvider outputProvider,
		CoinJoinCoinSelector coinJoinCoinSelector,
		CoinJoinConfiguration coinJoinConfiguration,
		LiquidityClueProvider liquidityClueProvider)
	{
		_keyChain = keyChain;
		_outputProvider = outputProvider;
		_liquidityClueProvider = liquidityClueProvider;
		_coinJoinConfiguration = coinJoinConfiguration;
		_coinJoinCoinSelector = coinJoinCoinSelector;
		_secureRandom = new SecureRandom();
	}

	public ImmutableList<SmartCoin> CoinsInCriticalPhase { get; private set; } = ImmutableList<SmartCoin>.Empty;

	private readonly SecureRandom _secureRandom;
	private readonly IKeyChain _keyChain;
	private readonly OutputProvider _outputProvider;
	private readonly LiquidityClueProvider _liquidityClueProvider;
	private readonly CoinJoinConfiguration _coinJoinConfiguration;
	private readonly CoinJoinCoinSelector _coinJoinCoinSelector;

	// NOTE: My additions
	public Dictionary<Guid, InputRegistrationRequestData> PendingInputRegistrationRequests;
	public Dictionary<Guid, ConnectionConfirmationRequestData> PendingConnectionConfirmationRequests;

	public IEnumerable<SmartCoin> StartCoinJoin(RoundState currentRoundState, IEnumerable<SmartCoin> coinCandidates)
	{
		Debug.Assert(coinCandidates.Any());

		RoundParameters roundParameters = currentRoundState.CoinjoinState.Parameters;

		if (roundParameters.MiningFeeRate.SatoshiPerByte > _coinJoinConfiguration.MaxCoinJoinMiningFeeRate)
		{
			string roundSkippedMessage = $"Mining fee rate was {roundParameters.MiningFeeRate} but max allowed is {_coinJoinConfiguration.MaxCoinJoinMiningFeeRate}.";
			currentRoundState.LogInfo(roundSkippedMessage);
			throw new CoinJoinClientException(CoinjoinError.MiningFeeRateTooHigh, roundSkippedMessage);
		}
		if (roundParameters.MinInputCountByRound < _coinJoinConfiguration.AbsoluteMinInputCount)
		{
			string roundSkippedMessage = $"Min input count for the round was {roundParameters.MinInputCountByRound} but min allowed is {_coinJoinConfiguration.AbsoluteMinInputCount}.";
			currentRoundState.LogInfo(roundSkippedMessage);
			throw new CoinJoinClientException(CoinjoinError.MinInputCountTooLow, roundSkippedMessage);
		}
		
		var liquidityClue = _liquidityClueProvider.GetLiquidityClue(roundParameters.MaxSuggestedAmount);
		var utxoSelectionParameters = UtxoSelectionParameters.FromRoundParameters(roundParameters, _outputProvider.DestinationProvider.SupportedScriptTypes.ToArray());

		ImmutableList<SmartCoin> coins = _coinJoinCoinSelector.SelectCoinsForRound(coinCandidates, utxoSelectionParameters, liquidityClue);

		// TTODO: Mixer.v1
		if (!roundParameters.AllowedInputTypes.Contains(ScriptType.P2WPKH) || !roundParameters.AllowedOutputTypes.Contains(ScriptType.P2WPKH))
		{
			currentRoundState.LogInfo("Skipping the round since it doesn't support P2WPKH inputs and outputs.");
			
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
			
			AliceClient aliceClient = new(response.AliceId, requestData.RoundState, requestData.Coin);
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
				AliceClient: alice);
			// TODO: Other versions have amountToRequest and vsizeToRequest 
			// stored separately in this record, so remove those
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
		
		IEnumerable<Coin> registeredCoins = registeredAliceClients.Select(alice => alice.SmartCoin.Coin);
		
		List<long> availableVsizes = registeredAliceClients.Select(alice => alice.IssuedVsizeCredentialsValue).ToList();
		foreach (long vsize in availableVsizes) Debug.Assert(vsize > 0);
		
		ConstructionState constructionState = roundState.Assert<ConstructionState>();
		
		var (ourCoins, theirCoins) = constructionState.Inputs.Partition(coin => registeredCoins.Any(ourCoin => coin.Outpoint == ourCoin.Outpoint));
		
		IEnumerable<Money> registeredCoinEffectiveValues = registeredAliceClients.Select(alice => alice.EffectiveValue);
		IEnumerable<Money> theirCoinEffectiveValues = theirCoins.Select(coin => coin.EffectiveValue(roundParameters.MiningFeeRate));
		
		// Verify that prevout information is correct for our own inputs.
		if (!registeredCoins.All(registeredCoin =>
			    ourCoins.Any(ourCoin =>
				    ourCoin.TxOut.ScriptPubKey.Hash == registeredCoin.TxOut.ScriptPubKey.Hash &&
				    ourCoin.TxOut.Value == registeredCoin.TxOut.Value)))
		{
			throw new CoinJoinClientException(CoinjoinError.CoordinatorLiedAboutInputs, "Coordinator lied about registered inputs. It probably tries to be malicious.");
		}
		
		ImmutableArray<TxOut> outputTxOuts = _outputProvider.GetOutputs(roundState.Id, roundParameters, registeredCoinEffectiveValues, theirCoinEffectiveValues, (int)availableVsizes.Sum()).ToImmutableArray();
		
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
		// see more: https://github.com/WalletWasabi/WalletWasabi/issues/8171
		bool isItSoloCoinjoin =  signingState.Inputs.Count() == registeredAliceClients.Length;
		bool isItForbiddenSoloCoinjoining = isItSoloCoinjoin && !_coinJoinConfiguration.AllowSoloCoinjoining;
		if (isItForbiddenSoloCoinjoining)
		{
			roundState.LogInfo($"I am the only one in that coinjoin.");
		}
		bool allMyOutputsArePresent = SanityCheck(outputTxOuts, unsignedCoinJoin.Transaction.Outputs);
		
		if (!allMyOutputsArePresent)
		{
			roundState.LogInfo($"There are missing outputs.");
		}
		
		// Assert that the effective fee rate is at least what was agreed on.
		// Otherwise, coordinator could take some of the mining fees for itself.
		// There is a tolerance because before constructing the transaction only an estimation can be computed.
		bool isCoordinatorTakingExtraFees = signingState.EffectiveFeeRate.FeePerK.Satoshi <= signingState.Parameters.MiningFeeRate.FeePerK.Satoshi * 0.90;
		if (isCoordinatorTakingExtraFees)
		{
			roundState.LogInfo($"Effective fee rate of the transaction is lower than expected.");
		}
		
		bool mustSignAllInputs = !isItForbiddenSoloCoinjoining && allMyOutputsArePresent && !isCoordinatorTakingExtraFees;
		if (!mustSignAllInputs)
		{
			roundState.LogInfo($"A subset of inputs will be signed.	");
		}
		
		ImmutableArray<AliceClient> alicesToSign = mustSignAllInputs
			? registeredAliceClients
			: registeredAliceClients.RemoveAt(_secureRandom.GetInt(0, registeredAliceClients.Length));
		
		TransactionSignaturesRequest[] sigRequests = new TransactionSignaturesRequest[alicesToSign.Length];
		for (int i = 0; i < alicesToSign.Length; i++)
		{
			Coin coin = alicesToSign[i].SmartCoin.Coin;
			var signedCoinJoin = _keyChain.Sign(unsignedCoinJoin.Transaction, coin, unsignedCoinJoin.PrecomputedTransactionData);
			var txInput = signedCoinJoin.Inputs.AsIndexedInputs().First(input => input.PrevOut == coin.Outpoint);
			if (!txInput.VerifyScript(coin, ScriptVerify.Standard, unsignedCoinJoin.PrecomputedTransactionData, out var error))
			{
				throw new InvalidOperationException($"Witness is missing. Reason {nameof(ScriptError)} code: {error}.");
			}
			sigRequests[i] = new TransactionSignaturesRequest(roundState.Id, txInput.Index, txInput.WitScript);
		}
		return sigRequests;
	}
}

// NOTE: Record to use when handling input registration response
public record InputRegistrationRequestData(InputRegistrationRequest Request, RoundState RoundState, SmartCoin Coin);

// TTODO: Mixer.v1
public record InputRegistrationRequestWithId(InputRegistrationRequest Request, Guid Id);

public record InputRegistrationResponseWithId(InputRegistrationResponse Response, Guid Id);

public record InputRegistrationResponseHandlingResult(List<AliceClient> AliceClients, List<SmartCoin> NotRegisteredCoins);

// NOTE: Record to use when handling connection confirmation response
public record ConnectionConfirmationRequestData(ConnectionConfirmationRequest Request, AliceClient AliceClient);

// TTODO: Mixer.v1
public record ConnectionConfirmationRequestWithId(ConnectionConfirmationRequest Request, Guid Id);

public record ConnectionConfirmationResponseWithId(ConnectionConfirmationResponse Response, Guid Id);