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
using Soju.Crypto;
using Soju.Exceptions;
using Soju.Extensions;
using Soju.Helpers;
using Soju.Logging;
using Soju.Models;
using Soju.WabiSabi.Backend.Models;
using Soju.WabiSabi.Backend.PostRequests;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client.CredentialDependencies;
using Soju.WabiSabi.Client.StatusChangedEvents;
using Soju.WabiSabi.Models;
using Soju.WabiSabi.Models.MultipartyTransaction;
using WabiSabi.CredentialRequesting;
using WabiSabi.Crypto.ZeroKnowledge;

namespace Soju.WabiSabi.Client.CoinJoin.Client;

public class CoinJoinClient
{
	private static readonly Money MinimumOutputAmountSanity = Money.Coins(0.0001m); // ignore rounds with too big minimum denominations

	public CoinJoinClient(
		IKeyChain keyChain,
		OutputProvider outputProvider,
		CoinJoinCoinSelector coinJoinCoinSelector,
		CoinJoinConfiguration coinJoinConfiguration,
		LiquidityClueProvider liquidityClueProvider,
		TimeSpan feeRateMedianTimeFrame = default,
		CoinjoinSkipFactors? skipFactors = null)
	{
		_keyChain = keyChain;
		_outputProvider = outputProvider;
		_liquidityClueProvider = liquidityClueProvider;
		_coinJoinConfiguration = coinJoinConfiguration;
		_coinJoinCoinSelector = coinJoinCoinSelector;
		_feeRateMedianTimeFrame = feeRateMedianTimeFrame;
		_skipFactors = skipFactors ?? CoinjoinSkipFactors.NoSkip;
		_secureRandom = new SecureRandom();
		
		PendingInputRegistrationRequests = new Dictionary<Guid, InputRegistrationRequestData>{};
		PendingConnectionConfirmationRequests = new Dictionary<Guid, ConnectionConfirmationRequestData>{};
	}

	public ImmutableList<SmartCoin> CoinsInCriticalPhase { get; private set; } = ImmutableList<SmartCoin>.Empty;

	private readonly SecureRandom _secureRandom;
	private readonly IKeyChain _keyChain;
	private readonly OutputProvider _outputProvider;
	private readonly LiquidityClueProvider _liquidityClueProvider;
	private readonly CoinJoinConfiguration _coinJoinConfiguration;
	private readonly CoinJoinCoinSelector _coinJoinCoinSelector;
	private readonly TimeSpan _feeRateMedianTimeFrame;
	private readonly CoinjoinSkipFactors _skipFactors;
	
	// NOTE: My additions
	public Dictionary<Guid, InputRegistrationRequestData> PendingInputRegistrationRequests;
	public Dictionary<Guid, ConnectionConfirmationRequestData> PendingConnectionConfirmationRequests;

	public IEnumerable<SmartCoin> StartCoinJoin(RoundState currentRoundState, IEnumerable<SmartCoin> coinCandidates)
	{	
		Debug.Assert(coinCandidates.Any());

		RoundParameters roundParameters = currentRoundState.CoinjoinState.Parameters;
		
		// TODO:
		// if (!IsRoundEconomic(roundParameters.MiningFeeRate, _roundStatusUpdater.CoinJoinFeeRateMedians, _feeRateMedianTimeFrame))
		// {
		// 	string roundSkippedMessage = "Uneconomical round skipped.";
		// 	currentRoundState.LogInfo(roundSkippedMessage);
		// 	throw new CoinJoinClientException(CoinjoinError.UneconomicalRound, roundSkippedMessage);
		// }
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
		// TODO: Redo this, because it uses time
		// if (_skipFactors.ShouldSkipRoundRandomly(_secureRandom, roundParameters.MiningFeeRate, _roundStatusUpdater.CoinJoinFeeRateMedians, currentRoundState.Id))
		// {
		// 	string roundSkippedMessage = "Round skipped randomly for better privacy.";
		// 	currentRoundState.LogInfo(roundSkippedMessage);
		// 	throw new CoinJoinClientException(CoinjoinError.RandomlySkippedRound, roundSkippedMessage);
		// }

		var liquidityClue = _liquidityClueProvider.GetLiquidityClue(roundParameters.MaxSuggestedAmount);
		var utxoSelectionParameters = UtxoSelectionParameters.FromRoundParameters(roundParameters, _outputProvider.DestinationProvider.SupportedScriptTypes.ToArray());

		ImmutableList<SmartCoin> coins = _coinJoinCoinSelector.SelectCoinsForRound(coinCandidates, utxoSelectionParameters, liquidityClue);
		
		// TODO: This just means that we will not register any inputs
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

	internal static bool IsRoundEconomic(FeeRate roundFeeRate, Dictionary<TimeSpan, FeeRate> coinJoinFeeRateMedians, TimeSpan feeRateMedianTimeFrame)
	{
		if (feeRateMedianTimeFrame == default)
		{
			return true;
		}

		if (coinJoinFeeRateMedians.ContainsKey(feeRateMedianTimeFrame))
		{
			// Round is not economic if any TimeFrame lower than _feeRateMedianTimeFrame has a FeeRate lower than current round's FeeRate.
			// 0.5 satoshi difference is allowable, to avoid rounding errors.
			return coinJoinFeeRateMedians
				.Where(x => x.Key <= feeRateMedianTimeFrame)
				.All(lowerTimeFrame => roundFeeRate.SatoshiPerByte <= lowerTimeFrame.Value.SatoshiPerByte + 0.5m);
		}

		throw new InvalidOperationException($"Could not find median fee rate for time frame: {feeRateMedianTimeFrame}.");
	}
	
	public List<InputRegistrationRequestWithId> CreateInputRegistrationRequests(IEnumerable<SmartCoin> smartCoins, RoundState roundState) 
	{
		List<InputRegistrationRequestWithId> requests = [];
		
		foreach (SmartCoin coin in smartCoins) 
		{
			try 
			{
				ArenaClient aliceArenaClient = new ArenaClient(
					roundState.CreateAmountCredentialClient(_secureRandom),
					roundState.CreateVsizeCredentialClient(_secureRandom),
					_coinJoinConfiguration.CoordinatorIdentifier);
				
				OwnershipProof ownershipProof = _keyChain.GetOwnershipProof(
					coin,
					new CoinJoinInputCommitmentData(aliceArenaClient.CoordinatorIdentifier, roundState.Id));
				
				ZeroCredentialsRequestData zeroAmountCredentialRequestData = aliceArenaClient.AmountCredentialClient.CreateRequestForZeroAmount();
				ZeroCredentialsRequestData zeroVsizeCredentialRequestData = aliceArenaClient.VsizeCredentialClient.CreateRequestForZeroAmount();
				
				InputRegistrationRequest request = new (
					roundState.Id,
					coin.Coin.Outpoint,
					ownershipProof,
					zeroAmountCredentialRequestData.CredentialsRequest,
					zeroVsizeCredentialRequestData.CredentialsRequest);
				
				Guid requestGuid = Guid.NewGuid();
				this.PendingInputRegistrationRequests[requestGuid] = new InputRegistrationRequestData(
					Request: request,
					Client: aliceArenaClient,
					RoundState: roundState,
					Coin: coin,
					ZeroAmountCredsRequest: zeroAmountCredentialRequestData,
					ZeroVsizeCredsRequest: zeroVsizeCredentialRequestData); 
				
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
			ArenaClient arenaClient = requestData.Client;
			
			IEnumerable<Credential>? realAmountCredentials = arenaClient.AmountCredentialClient.HandleResponse(
																response.AmountCredentials, 
																requestData.ZeroAmountCredsRequest.CredentialsResponseValidation);
			IEnumerable<Credential>? realVsizeCredentials = arenaClient.VsizeCredentialClient.HandleResponse(
																response.VsizeCredentials, 
																requestData.ZeroVsizeCredsRequest.CredentialsResponseValidation);
			
			// NOTE: We could directly input the data into AliceClient, but let's go through ArenaResponse's constructor
			ArenaResponse<Guid> arenaResponse = new(response.AliceId, realAmountCredentials, realVsizeCredentials);
			AliceClient aliceClient = new(arenaResponse.Value, requestData.RoundState, arenaClient, requestData.Coin, 
				arenaResponse.IssuedAmountCredentials, arenaResponse.IssuedVsizeCredentials);
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
	
	public record ConnectionConfirmationRequestData(ConnectionConfirmationRequest Request, AliceClient AliceClient, ZeroCredentialsRequestData ZeroAmountCredsRequest, ZeroCredentialsRequestData ZeroVsizeCredsRequest, RealCredentialsRequestData RealAmountCredsRequest, RealCredentialsRequestData RealVsizeCredsRequest);
	
	// TODO: Why do we need alices as a parameter? Wouldn't it be better to just store alices?
	public List<ConnectionConfirmationRequestWithId> CreateConnectionConfirmationRequests(List<AliceClient> alices)
	{
		List<ConnectionConfirmationRequestWithId> requests = [];
		foreach (AliceClient alice in alices)
		{
			long[] amountsToRequest = { alice.EffectiveValue.Satoshi };
			long[] vsizesToRequest = { alice.MaxVsizeAllocationPerAlice - alice.SmartCoin.ScriptPubKey.EstimateInputVsize() };
			
			uint256 roundId = alice.RoundId;
			Guid aliceId = alice.AliceId;
			IEnumerable<Credential> amountCredentialsToPresent = alice.IssuedAmountCredentials;
			IEnumerable<Credential> vsizeCredentialsToPresent = alice.IssuedVsizeCredentials;
			ArenaClient arenaClient = alice.ArenaClient;
			
			Guard.InRange(nameof(amountsToRequest), amountsToRequest, 1, ProtocolConstants.CredentialNumber);
			Guard.InRange(nameof(amountCredentialsToPresent), amountCredentialsToPresent, 0, ProtocolConstants.CredentialNumber);
			Guard.InRange(nameof(vsizeCredentialsToPresent), vsizeCredentialsToPresent, 0, ProtocolConstants.CredentialNumber);
			Guard.InRange(nameof(vsizesToRequest), vsizesToRequest, 1, arenaClient.VsizeCredentialClient.NumberOfCredentials);
			
			// TODO: We are creating a new cancellation token just to satisfy the API. This
			// shouldn't matter, because CreateRequest just calls InternalCreateRequest,
			// which is synchronous and doesn't use cancellationToken in any way. However,
			// it would be good to look into if we should use WabiSabi statically.
			CancellationToken fakeCancellationToken = new();
			
			RealCredentialsRequestData realAmountCredentialRequestData = arenaClient.AmountCredentialClient.CreateRequest(
				amountsToRequest,
				amountCredentialsToPresent,
				fakeCancellationToken);
			
			RealCredentialsRequestData realVsizeCredentialRequestData = arenaClient.VsizeCredentialClient.CreateRequest(
				vsizesToRequest,
				vsizeCredentialsToPresent,
				fakeCancellationToken);
			
			ZeroCredentialsRequestData zeroAmountCredentialRequestData = arenaClient.AmountCredentialClient.CreateRequestForZeroAmount();
			ZeroCredentialsRequestData zeroVsizeCredentialRequestData = arenaClient.VsizeCredentialClient.CreateRequestForZeroAmount();
			
			ConnectionConfirmationRequest request = new (
				roundId,
				aliceId,
				zeroAmountCredentialRequestData.CredentialsRequest,
				realAmountCredentialRequestData.CredentialsRequest,
				zeroVsizeCredentialRequestData.CredentialsRequest,
				realVsizeCredentialRequestData.CredentialsRequest);
			
			Guid requestGuid = Guid.NewGuid();
			this.PendingConnectionConfirmationRequests[requestGuid] = new ConnectionConfirmationRequestData(
				Request: request,
				AliceClient: alice,
				zeroAmountCredentialRequestData,
				zeroVsizeCredentialRequestData,
				realAmountCredentialRequestData,
				realVsizeCredentialRequestData);
			requests.Add(new ConnectionConfirmationRequestWithId(request, requestGuid));
		}
		return requests;
	}
	
	public List<AliceClient> HandleConnectionConfirmationResponses(ICollection<ConnectionConfirmationResponseWithId> responses)
	{
		List<AliceClient> alices = [];
		
		// AliceClient[] alices = new AliceClient[responses.Count];
		
		foreach ((ConnectionConfirmationResponse response, Guid id) in responses)
		{
			ConnectionConfirmationRequestData requestData = this.PendingConnectionConfirmationRequests[id];
			
			ArenaClient arenaClient = requestData.AliceClient.ArenaClient;
			
			IEnumerable<Credential> zeroAmountCredentials = arenaClient.AmountCredentialClient.HandleResponse(
				response.ZeroAmountCredentials, 
				requestData.ZeroAmountCredsRequest.CredentialsResponseValidation);
			IEnumerable<Credential> zeroVsizeCredentials = arenaClient.VsizeCredentialClient.HandleResponse(
				response.ZeroVsizeCredentials, 
				requestData.ZeroVsizeCredsRequest.CredentialsResponseValidation);
			
			ArenaResponse<bool> arenaResponse;
			if (response is {RealAmountCredentials: {}, RealVsizeCredentials: {}})
			{
				var realAmountCredentials = arenaClient.AmountCredentialClient.HandleResponse(
					response.RealAmountCredentials,
					requestData.RealAmountCredsRequest.CredentialsResponseValidation);
				var realVsizeCredentials = arenaClient.VsizeCredentialClient.HandleResponse(
					response.RealVsizeCredentials, 
					requestData.RealVsizeCredsRequest.CredentialsResponseValidation);
				arenaResponse = new(true, realAmountCredentials, realVsizeCredentials);
			} else {
				arenaResponse = new(false, zeroAmountCredentials, zeroVsizeCredentials);
			}
			
			AliceClient aliceClient = requestData.AliceClient;
			aliceClient.IssuedAmountCredentials = arenaResponse.IssuedAmountCredentials;
			aliceClient.IssuedVsizeCredentials = arenaResponse.IssuedVsizeCredentials;
			
			// TODO: Catch WabiSabiProtocolExceptions
			// TODO: Differentiate between pending and zerocredentials, plus somehow measure it
			alices.Add(aliceClient);
			PendingConnectionConfirmationRequests.Remove(id);
		}
		PendingConnectionConfirmationRequests.Clear();
		return alices;
	}
	
	// NOTE: Done like this, because both output and dependency graph calculations can take 
	// some time and we can do that in parallel with other clients. Then later we need to 
	// create reissuance according to the graph, but that is a lot more back and forth.
	// TODO: Not the prettiest return values
	public (ImmutableArray<TxOut>, DependencyGraph) CreateOutputsAndDependencyGraph(RoundState roundState, ImmutableArray<AliceClient> registeredAliceClients)
	{
		Debug.Assert(roundState.Phase == Phase.OutputRegistration);
		
		RoundParameters roundParameters = roundState.CoinjoinState.Parameters;
		
		IEnumerable<Coin> registeredCoins = registeredAliceClients.Select(alice => alice.SmartCoin.Coin);
		IEnumerable<long> availableVsizes = registeredAliceClients.SelectMany(alice => alice.IssuedVsizeCredentials.Where(cred => cred.Value > 0)).Select(cread => cread.Value);
		
		ConstructionState constructionState = roundState.Assert<ConstructionState>();
		
		IEnumerable<Coin> theirCoins = constructionState.Inputs.Where(coin => !registeredCoins.Any(myCoin => coin.Outpoint == myCoin.Outpoint));
		IEnumerable<Money> registeredCoinEffectiveValues = registeredAliceClients.Select(alice => alice.EffectiveValue);
		IEnumerable<Money> theirCoinEffectiveValues = theirCoins.Select(coin => coin.EffectiveValue(roundParameters.MiningFeeRate));
		
		ImmutableArray<TxOut> outputTxOuts = _outputProvider.GetOutputs(roundState.Id, roundParameters, registeredCoinEffectiveValues, theirCoinEffectiveValues, (int)availableVsizes.Sum()).ToImmutableArray();
		
		DependencyGraph dependencyGraph = DependencyGraph.ResolveCredentialDependencies(
			registeredCoinEffectiveValues, 
			outputTxOuts, 
			roundParameters.MiningFeeRate, 
			availableVsizes, 
			roundParameters.MaxAmountCredentialValue, 
			roundParameters.MaxVsizeCredentialValue);
		
		return (outputTxOuts, dependencyGraph);
	}
	
	// TODO: IMPORTANT: hack
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
		
		// Send signature.
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
public record InputRegistrationRequestData(InputRegistrationRequest Request, ArenaClient Client, RoundState RoundState, 
	SmartCoin Coin, ZeroCredentialsRequestData ZeroAmountCredsRequest, ZeroCredentialsRequestData ZeroVsizeCredsRequest);
	
// TODO: This doesn't need to be Guid; it only needs to be unique for this CoinJoinClient
public record InputRegistrationRequestWithId(InputRegistrationRequest Request, Guid Id);

public record InputRegistrationResponseWithId(InputRegistrationResponse Response, Guid Id);

public record InputRegistrationResponseHandlingResult(List<AliceClient> AliceClients, List<SmartCoin> NotRegisteredCoins);

// TODO: The same as with InputRegistrationRequestWithId
public record ConnectionConfirmationRequestWithId(ConnectionConfirmationRequest Request, Guid Id);

public record ConnectionConfirmationResponseWithId(ConnectionConfirmationResponse Response, Guid Id);

