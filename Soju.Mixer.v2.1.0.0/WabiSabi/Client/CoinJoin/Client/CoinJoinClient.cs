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
using Soju.Extensions;
using Soju.Helpers;
using Soju.Logging;
using Soju.Models;
using Soju.WabiSabi.Backend.Models;
using Soju.WabiSabi.Backend.PostRequests;
using Soju.WabiSabi.Backend.Rounds;
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
		KeyChain = keyChain;
		OutputProvider = outputProvider;
		LiquidityClueProvider = liquidityClueProvider;
		CoinJoinConfiguration = coinJoinConfiguration;
		CoinJoinCoinSelector = coinJoinCoinSelector;
		FeeRateMedianTimeFrame = feeRateMedianTimeFrame;
		SkipFactors = skipFactors ?? CoinjoinSkipFactors.NoSkip;
		SecureRandom = new SecureRandom();
		
		PendingInputRegistrationRequests = new Dictionary<Guid, InputRegistrationRequestData>{};
		PendingConnectionConfirmationRequests = new Dictionary<Guid, ConnectionConfirmationRequestData>{};
	}

	public ImmutableList<SmartCoin> CoinsInCriticalPhase { get; private set; } = ImmutableList<SmartCoin>.Empty;

	private SecureRandom SecureRandom { get; }
	private IKeyChain KeyChain { get; }
	private OutputProvider OutputProvider { get; }
	private LiquidityClueProvider LiquidityClueProvider { get; }
	private CoinJoinConfiguration CoinJoinConfiguration { get; }
	private CoinJoinCoinSelector CoinJoinCoinSelector { get; }
	private TimeSpan FeeRateMedianTimeFrame { get; }
	private CoinjoinSkipFactors SkipFactors { get; }
	
	// NOTE: My additions
	public Dictionary<Guid, InputRegistrationRequestData> PendingInputRegistrationRequests;
	public Dictionary<Guid, ConnectionConfirmationRequestData> PendingConnectionConfirmationRequests;
	
	public IEnumerable<SmartCoin> StartCoinJoin(RoundState currentRoundState, IEnumerable<SmartCoin> coinCandidates)
	{	
		Debug.Assert(coinCandidates.Any());
		
		RoundParameters roundParameters = currentRoundState.CoinjoinState.Parameters;
		
		// TTODO: Mixer.v1
		// if (!IsRoundEconomic(roundParameters.MiningFeeRate, RoundStatusUpdater.CoinJoinFeeRateMedians, FeeRateMedianTimeFrame))
		// {
		// 	string roundSkippedMessage = "Uneconomical round skipped.";
		// 	currentRoundState.LogInfo(roundSkippedMessage);
		// 	throw new CoinJoinClientException(CoinjoinError.UneconomicalRound, roundSkippedMessage);
		// }
		if (roundParameters.CoordinationFeeRate.Rate > CoinJoinConfiguration.MaxCoordinationFeeRate)
		{
			string roundSkippedMessage = $"Coordination fee rate was {roundParameters.CoordinationFeeRate.Rate} but max allowed is {CoinJoinConfiguration.MaxCoordinationFeeRate}.";
			currentRoundState.LogInfo(roundSkippedMessage);
			throw new CoinJoinClientException(CoinjoinError.CoordinationFeeRateTooHigh, roundSkippedMessage);
		}
		if (roundParameters.MiningFeeRate.SatoshiPerByte > CoinJoinConfiguration.MaxCoinJoinMiningFeeRate)
		{
			string roundSkippedMessage = $"Mining fee rate was {roundParameters.MiningFeeRate} but max allowed is {CoinJoinConfiguration.MaxCoinJoinMiningFeeRate}.";
			currentRoundState.LogInfo(roundSkippedMessage);
			throw new CoinJoinClientException(CoinjoinError.MiningFeeRateTooHigh, roundSkippedMessage);
		}
		if (roundParameters.MinInputCountByRound < CoinJoinConfiguration.AbsoluteMinInputCount)
		{
			string roundSkippedMessage = $"Min input count for the round was {roundParameters.MinInputCountByRound} but min allowed is {CoinJoinConfiguration.AbsoluteMinInputCount}.";
			currentRoundState.LogInfo(roundSkippedMessage);
			throw new CoinJoinClientException(CoinjoinError.MinInputCountTooLow, roundSkippedMessage);
		}
		// TTODO: Mixer.v1
		// if (SkipFactors.ShouldSkipRoundRandomly(SecureRandom, roundParameters.MiningFeeRate, RoundStatusUpdater.CoinJoinFeeRateMedians, currentRoundState.Id))
		// {
		// 	string roundSkippedMessage = "Round skipped randomly for better privacy.";
		// 	currentRoundState.LogInfo(roundSkippedMessage);
		// 	throw new CoinJoinClientException(CoinjoinError.RandomlySkippedRound, roundSkippedMessage);
		// }
		
		var liquidityClue = LiquidityClueProvider.GetLiquidityClue(roundParameters.MaxSuggestedAmount);
		var utxoSelectionParameters = UtxoSelectionParameters.FromRoundParameters(roundParameters, OutputProvider.DestinationProvider.SupportedScriptTypes.ToArray());

		ImmutableList<SmartCoin> coins = CoinJoinCoinSelector.SelectCoinsForRound(coinCandidates, utxoSelectionParameters, liquidityClue);
		
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
		
		IEnumerable<Coin> registeredCoins = registeredAliceClients.Select(alice => alice.SmartCoin.Coin);
		
		long availableVsize = registeredAliceClients.Sum(alice => alice.IssuedVsizeCredentialsValue);
		Debug.Assert(availableVsize > 0);
		
		ConstructionState constructionState = roundState.Assert<ConstructionState>();
		
		IEnumerable<Coin> theirCoins = constructionState.Inputs.Where(coin => !registeredCoins.Any(myCoin => coin.Outpoint == myCoin.Outpoint));
		IEnumerable<Money> registeredCoinEffectiveValues = registeredAliceClients.Select(alice => alice.EffectiveValue);
		IEnumerable<Money> theirCoinEffectiveValues = theirCoins.Select(coin => coin.EffectiveValue(roundParameters.MiningFeeRate, roundParameters.CoordinationFeeRate));
		
		ImmutableArray<TxOut> outputTxOuts = OutputProvider.GetOutputs(roundState.Id, roundParameters, registeredCoinEffectiveValues, theirCoinEffectiveValues, (int)availableVsize).ToImmutableArray();
		
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
		bool isItForbiddenSoloCoinjoining = isItSoloCoinjoin && !CoinJoinConfiguration.AllowSoloCoinjoining;
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
}

// NOTE: Record to use when handling input registration response
public record InputRegistrationRequestData(InputRegistrationRequest Request, RoundState RoundState, SmartCoin Coin);
	
// TTODO: Mixer.v1
public record InputRegistrationRequestWithId(InputRegistrationRequest Request, Guid Id);
	
public record InputRegistrationResponseWithId(InputRegistrationResponse Response, Guid Id);
	
public record InputRegistrationResponseHandlingResult(List<AliceClient> AliceClients, List<SmartCoin> NotRegisteredCoins);
	
// NOTE: Record to use when handling connection confirmation response
public record ConnectionConfirmationRequestData(ConnectionConfirmationRequest Request, AliceClient AliceClient, long RealAmountCredsRequest, long RealVsizeCredsRequest);
	
// TTODO: Mixer.v1
public record ConnectionConfirmationRequestWithId(ConnectionConfirmationRequest Request, Guid Id);
	
public record ConnectionConfirmationResponseWithId(ConnectionConfirmationResponse Response, Guid Id);