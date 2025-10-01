using System.Collections.Generic;
using NBitcoin;
using System.Diagnostics;
using WabiSabi.CredentialRequesting;
using WabiSabi.Crypto;
using Soju.WabiSabi.Backend.Models;
using Soju.WabiSabi.Backend.PostRequests;
using Soju.WabiSabi.Models;
using Soju.WabiSabi.Models.MultipartyTransaction;
using Soju.Logging;
using Soju.Crypto.Randomness;

namespace Soju.WabiSabi.Backend.Rounds;

public partial class Arena : IWabiSabiApiRequestHandler
{
	public InputRegistrationResponse RegisterInput(InputRegistrationRequest request)
	{
		try
		{
			return RegisterInputCore(request);
		}
		catch (Exception ex) when (IsUserCheating(ex))
		{
			Logger.LogInfo($"{request.Input} is cheating: {ex.Message}");
			// TODO: 
			// _prison.CheatingDetected(request.Input, request.RoundId);
			throw;
		}
	}

	private InputRegistrationResponse RegisterInputCore(InputRegistrationRequest request)
	{
		var (coin, confirmations) = OutpointToCoin(request);

		var round = GetRound(request.RoundId);

		// Compute but don't commit updated coinjoin to round state, it will
		// be re-calculated on input confirmation. This is computed in here
		// for validation purposes.
		_ = round.Assert<ConstructionState>().AddInput(coin, request.OwnershipProof, round.CoinJoinInputCommitmentData);

		CheckCoinIsNotBanned(coin.Outpoint, round);
		
		// NOTE: EndRoundState.TransactionBroadcasted is also selected, I guess,
		// because it was broadcasted recently. (as broadcasted rounds get removed
		// in Arena.TimeoutRounds -- we don't have any timeout time and do it
		// immediately so this condition could be changed)
		var registeredCoins = Rounds.Where(x => !(x.Phase == Phase.Ended && x.EndRoundState != EndRoundState.TransactionBroadcasted))
			.SelectMany(r => r.Alices.Select(a => a.Coin));

		if (registeredCoins.Any(x => x.Outpoint == coin.Outpoint))
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceAlreadyRegistered);
		}

		if (round.IsInputRegistrationEnded(_config.MaxInputCountByRound))
		{
			throw new WrongPhaseException(round, Phase.InputRegistration);
		}

		// Generate a new GUID with the secure random source, to be sure
		// that it is not guessable (Guid.NewGuid() documentation does
		// not say anything about GUID version or randomness source,
		// only that the probability of duplicates is very low).
		var id = new Guid(SecureRandom.Instance.GetBytes(16));

		var alice = new Alice(coin, request.OwnershipProof, round, id);

		if (alice.CalculateRemainingAmountCredentials(round.Parameters.MiningFeeRate) <= Money.Zero)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.UneconomicalInput);
		}

		if (alice.TotalInputAmount < round.Parameters.MinAmountCredentialValue)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.NotEnoughFunds);
		}
		if (alice.TotalInputAmount > round.Parameters.MaxAmountCredentialValue)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.TooMuchFunds);
		}

		if (alice.TotalInputVsize > round.Parameters.MaxVsizeAllocationPerAlice)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.TooMuchVsize);
		}

		if (round.RemainingInputVsizeAllocation < round.Parameters.MaxVsizeAllocationPerAlice)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.VsizeQuotaExceeded);
		}
		
		var commitAmountCredentialResponse = round.AmountCredentialIssuer.HandleRequest(request.ZeroAmountCredentialRequests);
		var commitVsizeCredentialResponse = round.VsizeCredentialIssuer.HandleRequest(request.ZeroVsizeCredentialRequests);

		round.Alices.Add(alice);

		return new(alice.Id,
			commitAmountCredentialResponse,
			commitVsizeCredentialResponse);
	}

	public void ReadyToSign(ReadyToSignRequestRequest request)
	{
		var round = GetRound(request.RoundId, Phase.OutputRegistration);
		var alice = GetAlice(request.AliceId, round);
		alice.ReadyToSign = true;
	}

	public void RemoveInput(InputsRemovalRequest request)
	{
		var round = GetRound(request.RoundId, Phase.InputRegistration);

		round.Alices.RemoveAll(x => x.Id == request.AliceId && x.ConfirmedConnection == false);
	}

	public ConnectionConfirmationResponse ConfirmConnection(ConnectionConfirmationRequest request)
	{
		try
		{
			return ConfirmConnectionCore(request);
		}
		catch (Exception ex) when (IsUserCheating(ex))
		{
			var round = GetRound(request.RoundId);
			var alice = GetAlice(request.AliceId, round);
			Logger.LogInfo($"{alice.Coin.Outpoint} is cheating: {ex.Message}");
			// TODO:
			// _prison.CheatingDetected(alice.Coin.Outpoint, request.RoundId);
			throw;
		}
	}

	private ConnectionConfirmationResponse ConfirmConnectionCore(ConnectionConfirmationRequest request)
	{
		Round round = GetRound(request.RoundId, Phase.ConnectionConfirmation);
		Alice alice = GetAlice(request.AliceId, round);
		var realAmountCredentialRequests = request.RealAmountCredentialRequests;
		var realVsizeCredentialRequests = request.RealVsizeCredentialRequests;

		if (alice.ConfirmedConnection)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceAlreadyConfirmedConnection, $"Round ({request.RoundId}): Alice ({request.AliceId}) already confirmed connection.");
		}

		if (realVsizeCredentialRequests.Delta != alice.CalculateRemainingVsizeCredentials(round.Parameters.MaxVsizeAllocationPerAlice))
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedVsizeCredentials, $"Round ({request.RoundId}): Incorrect requested vsize credentials.");
		}

		var remaining = alice.CalculateRemainingAmountCredentials(round.Parameters.MiningFeeRate);
		if (realAmountCredentialRequests.Delta != remaining)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedAmountCredentials, $"Round ({request.RoundId}): Incorrect requested amount credentials.");
		}

		CredentialsResponse amountZeroCredentialResponse = round.AmountCredentialIssuer.HandleRequest(request.ZeroAmountCredentialRequests);
		CredentialsResponse vsizeZeroCredentialResponse = round.VsizeCredentialIssuer.HandleRequest(request.ZeroVsizeCredentialRequests);

		
		if (round.Phase == Phase.ConnectionConfirmation)
		{
			// If the phase was InputRegistration before then we did not pre-calculate real credentials.
			CredentialsResponse amountRealCredentialResponse = round.AmountCredentialIssuer.HandleRequest(realAmountCredentialRequests);
			CredentialsResponse vsizeRealCredentialResponse = round.VsizeCredentialIssuer.HandleRequest(realVsizeCredentialRequests);

			ConnectionConfirmationResponse response = new(
				amountZeroCredentialResponse,
				vsizeZeroCredentialResponse,
				amountRealCredentialResponse,
				vsizeRealCredentialResponse);

			// Update the coinjoin state, adding the confirmed input.
			round.CoinjoinState = round.Assert<ConstructionState>().AddInput(alice.Coin, alice.OwnershipProof, round.CoinJoinInputCommitmentData);
			alice.ConfirmedConnection = true;
			return response;
		}
		else 
		{
			throw new WrongPhaseException(round, Phase.ConnectionConfirmation);
		}
	}

	public EmptyResponse RegisterOutput(OutputRegistrationRequest request)
	{
		return RegisterOutputCore(request);
	}

	public EmptyResponse RegisterOutputCore(OutputRegistrationRequest request)
	{
		var round = GetRound(request.RoundId, Phase.OutputRegistration);

		var credentialAmount = -request.AmountCredentialRequests.Delta;

		if (CoinJoinScriptStore?.Contains(request.Script) is true)
		{
			Logger.LogWarning($"Round ({request.RoundId}): Already registered script in previous coinjoins.");
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AlreadyRegisteredScript, $"Round ({request.RoundId}): Already registered script.");
		}

		var outputScripts = Rounds
			.Where(r => r.Id != round.Id && r.Phase != Phase.Ended)
			.SelectMany(r => r.Bobs)
			.Select(x => x.Script)
			.ToHashSet();
		if (outputScripts.Contains(request.Script))
		{
			Logger.LogWarning($"Round ({request.RoundId}): Already registered script in some round (output side).");
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AlreadyRegisteredScript, $"Round ({request.RoundId}): Already registered script in some round.");
		}

		var inputScripts = Rounds.SelectMany(r => round.Alices).Select(a => a.Coin.ScriptPubKey).ToHashSet();
		if (inputScripts.Contains(request.Script))
		{
			Logger.LogWarning($"Round ({request.RoundId}): Already registered script in some round (input side).");
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AlreadyRegisteredScript, $"Round ({request.RoundId}): Already registered script some round.");
		}

		Bob bob = new(request.Script, credentialAmount);

		var outputValue = bob.CalculateOutputAmount(round.Parameters.MiningFeeRate);

		var vsizeCredentialRequests = request.VsizeCredentialRequests;
		if (-vsizeCredentialRequests.Delta != bob.OutputVsize)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedVsizeCredentials, $"Round ({request.RoundId}): Incorrect requested vsize credentials.");
		}

		// Update the current round state with the additional output to ensure it's valid.
		var newState = round.AddOutput(new TxOut(outputValue, bob.Script));

		// Verify the credential requests and prepare their responses.
		round.AmountCredentialIssuer.HandleRequest(request.AmountCredentialRequests);
		round.VsizeCredentialIssuer.HandleRequest(vsizeCredentialRequests);

		// Update round state.
		round.Bobs.Add(bob);
		round.CoinjoinState = newState;

		return EmptyResponse.Instance;
	}

	public void SignTransaction(TransactionSignaturesRequest request)
	{
		var round = GetRound(request.RoundId, Phase.TransactionSigning);

		var state = round.Assert<SigningState>().AddWitness((int)request.InputIndex, request.Witness);

		// at this point all of the witnesses have been verified and the state can be updated
		round.CoinjoinState = state;
	}

	public ReissueCredentialResponse Reissuance(ReissueCredentialRequest request)
	{
		Round round = GetRound(request.RoundId, Phase.ConnectionConfirmation, Phase.OutputRegistration);

		if (request.RealAmountCredentialRequests.Delta != 0)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.DeltaNotZero, $"Round ({round.Id}): Amount credentials delta must be zero.");
		}

		if (request.RealVsizeCredentialRequests.Delta != 0)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.DeltaNotZero, $"Round ({round.Id}): Vsize credentials delta must be zero.");
		}

		if (request.RealAmountCredentialRequests.Requested.Count() != ProtocolConstants.CredentialNumber)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongNumberOfCreds, $"Round ({round.Id}): Incorrect requested number of amount credentials.");
		}

		if (request.RealVsizeCredentialRequests.Requested.Count() != ProtocolConstants.CredentialNumber)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongNumberOfCreds, $"Round ({round.Id}): Incorrect requested number of weight credentials.");
		}

		var realAmountResponse = round.AmountCredentialIssuer.HandleRequest(request.RealAmountCredentialRequests);
		var realVsizeResponse = round.VsizeCredentialIssuer.HandleRequest(request.RealVsizeCredentialRequests);
		var zeroAmountResponse = round.AmountCredentialIssuer.HandleRequest(request.ZeroAmountCredentialRequests);
		var zeroVsizeResponse = round.VsizeCredentialIssuer.HandleRequest(request.ZeroVsizeCredentialsRequests);

		return new(
			realAmountResponse,
			realVsizeResponse,
			zeroAmountResponse,
			zeroVsizeResponse);
	}

	public (Coin coin, int Confirmations) OutpointToCoin(InputRegistrationRequest request)
	{
		OutPoint input = request.Input;
		
		// NOTE: Given that in Soju clients don't cheat, this would be more likely a bug in the code
		var txOutResponse = Rpc.GetTxOut(input.Hash, (int)input.N)
			?? throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputSpent);
		if (txOutResponse.Confirmations == 0)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputUnconfirmed);
		}

		if (txOutResponse.IsCoinBase && txOutResponse.Confirmations <= 100)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputImmature);
		}

		return (new Coin(input, txOutResponse.TxOut), txOutResponse.Confirmations);
	}

	public RoundStateResponse GetStatus(RoundStateRequest request)
	{
		if (_config.IsCoordinationEnabled is false)
		{
			return new RoundStateResponse(Array.Empty<RoundState>(), Array.Empty<CoinJoinFeeRateMedian>());
		}
		var requestCheckPointDictionary = request.RoundCheckpoints.ToDictionary(r => r.RoundId, r => r);
		var responseRoundStates = RoundStates.Select(x =>
		{
			if (requestCheckPointDictionary.TryGetValue(x.Id, out RoundStateCheckpoint? checkPoint) && checkPoint.StateId > 0)
			{
				return x.GetSubState(checkPoint.StateId);
			}

			return x;
		}).ToArray();
		return new RoundStateResponse(responseRoundStates, Array.Empty<CoinJoinFeeRateMedian>());
	}

	public (uint256 RoundId, FeeRate MiningFeeRate)[] GetRoundsContainingOutpoints(IEnumerable<OutPoint> outPoints) =>
		Rounds
		.Where(r => r.Phase != Phase.Ended && r.Phase >= Phase.ConnectionConfirmation)
		.SelectMany(r => r.CoinjoinState.Inputs.Select(a => (RoundId: r.Id, MiningFeeRate: r.Parameters.MiningFeeRate, Coin: a)))
		.Where(x => outPoints.Any(outpoint => outpoint == x.Coin.Outpoint))
		.Select(x => (x.RoundId, x.MiningFeeRate))
		.Distinct()
		.ToArray();

	private void CheckCoinIsNotBanned(OutPoint input, Round round)
	{
		// TODO:
		return;
		// var banningTime = _prison.GetBanTimePeriod(input, _config.GetDoSConfiguration());
		// if (banningTime.Includes(DateTimeOffset.UtcNow))
		// {
		// 	round.LogInfo($"{input} rejected. Banned until {banningTime.EndTime}");
		// 	throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputBanned, exceptionData: new InputBannedExceptionData(banningTime.EndTime));
		// }
	}

	private Round GetRound(uint256 roundId) =>
		Rounds.FirstOrDefault(x => x.Id == roundId)
		?? throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({roundId}) not found.");

	private Round InPhase(Round round, Phase[] phases) =>
		phases.Contains(round.Phase)
		? round
		: throw new WrongPhaseException(round, phases);

	private Round GetRound(uint256 roundId, params Phase[] phases) =>
		InPhase(GetRound(roundId), phases);
	
	// TODO: Probably rewrite this to make Alices be dictionary
	private Alice GetAlice(Guid aliceId, Round round) =>
		round.Alices.Find(x => x.Id == aliceId)
		?? throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({round.Id}): Alice ({aliceId}) not found.");

	private static bool IsUserCheating(Exception e) =>
		e is WabiSabiCryptoException || (e is WabiSabiProtocolException wpe && wpe.ErrorCode.IsEvidencingClearMisbehavior());
}
