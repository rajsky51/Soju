using System.Collections.Concurrent;
using NBitcoin;
using Soju.BitcoinCore.Rpc;
using Soju.Crypto.Randomness;
using Soju.WabiSabi.Backend.Models;
using Soju.WabiSabi.Models.MultipartyTransaction;
using Soju.WabiSabi.Backend.Statistics;
using System.Collections.Immutable;
using Soju.WabiSabi.Models;
using Soju.Extensions;
using Soju.Logging;
using Soju.WabiSabi.Backend.DoSPrevention;
using Soju.MyNBitcoin;

namespace Soju.WabiSabi.Backend.Rounds;

public class Arena : IWabiSabiApiRequestHandler
{
    public HashSet<Round> Rounds { get; } = new();
    public ImmutableList<RoundState> RoundStates { get; private set; } = ImmutableList<RoundState>.Empty;
    internal ConcurrentQueue<uint256> DisruptedRounds { get; } = new();
    private readonly WabiSabiConfig _config;
    private readonly Prison _prison;
    public CoinJoinScriptStore? CoinJoinScriptStore { get; }
    private readonly RoundParameterFactory _roundParameterFactory;
    public MaxSuggestedAmountProvider MaxSuggestedAmountProvider { get; }
    
	// NOTE: Additions
	private readonly MyTransactionFactory _transactionFactory;  
    
    public Arena(
        WabiSabiConfig config,
        Prison prison,
        RoundParameterFactory roundParameterFactory,
        CoinJoinScriptStore? coinJoinScriptStore = null,
        TimeSpan? period = null
        )
    {
        _config = config;
        _prison = prison;
        CoinJoinScriptStore = coinJoinScriptStore;
        _roundParameterFactory = roundParameterFactory;
        MaxSuggestedAmountProvider = new(_config);
    }
    
    protected Task Action(CancellationToken cancel)
    {
            TimeoutRounds();

            TimeoutAlices();

            await StepTransactionSigningPhaseAsync(cancel).ConfigureAwait(false);

            StepOutputRegistrationPhase();

            await StepConnectionConfirmationPhaseAsync(cancel).ConfigureAwait(false);

            StepInputRegistrationPhase();

            cancel.ThrowIfCancellationRequested();

            // Ensure there's at least one non-blame round in input registration.
            CreateRound();

            AbortDisruptedRounds();

            // RoundStates have to contain all states. Do not change stateId=0.
            SetRoundStates();
    }
    
    private void StepInputRegistrationPhase()
    {
	    foreach (var round in Rounds.Where(x =>
			             x.Phase == Phase.InputRegistration
			             && x.IsInputRegistrationEnded(x.Parameters.MaxInputCountByRound))
		             .ToArray())
	    {
		    try
		    {
			    if (round.InputCount < round.Parameters.MinInputCountByRound)
			    {
				    MaxSuggestedAmountProvider.StepMaxSuggested(round, false);
				    EndRound(round, EndRoundState.AbortedNotEnoughAlices);
				    round.LogInfo($"Not enough inputs ({round.InputCount}) in {nameof(Phase.InputRegistration)} phase. The minimum is ({round.Parameters.MinInputCountByRound}). {nameof(round.Parameters.MaxSuggestedAmount)} was '{round.Parameters.MaxSuggestedAmount}' BTC.");
			    }
			    else if (round.IsInputRegistrationEnded(round.Parameters.MaxInputCountByRound))
			    {
				    MaxSuggestedAmountProvider.StepMaxSuggested(round, true);
				    SetRoundPhase(round, Phase.ConnectionConfirmation);
			    }
		    }
		    catch (Exception ex)
		    {
			    EndRound(round, EndRoundState.AbortedWithError);
			    round.LogError(ex.Message);
		    }
	    }
    }
    
    private void StepOutputRegistrationPhase()
    {
        foreach (var round in Rounds.Where(x => x.Phase == Phase.OutputRegistration).ToArray())
        {
            try
            {
                var coinjoin = round.Assert<ConstructionState>();

                round.LogInfo($"{coinjoin.Inputs.Count()} inputs were added.");
                round.LogInfo($"{coinjoin.Outputs.Count()} outputs were added.");

                round.CoordinatorScript = GetCoordinatorScriptPreventReuse(round);
                coinjoin = AddCoordinationFee(round, coinjoin, round.CoordinatorScript);

                round.CoinjoinState = FinalizeTransaction(round.Id, coinjoin);

                SetRoundPhase(round, Phase.TransactionSigning);
            }
            catch (Exception ex)
            {
                EndRound(round, EndRoundState.AbortedWithError);
                round.LogError(ex.Message);
            }
        }
    }
    
    private void CreateRound()
	{
		// TODO: This is a hack and feeRate should be configurable
		Money feePerK = Money.Satoshis(4 * 1000);
		FeeRate feeRate = new(feePerK);
		
		RoundParameters parameters = _roundParameterFactory.CreateRoundParameter(feeRate, MaxSuggestedAmountProvider.MaxSuggestedAmount);

		var r = new Round(parameters, SecureRandom.Instance);
		AddRound(r);
		r.LogInfo($"Created round with parameters: {nameof(r.Parameters.MaxSuggestedAmount)}:'{r.Parameters.MaxSuggestedAmount}' BTC.");
	}
    
    private void SetRoundPhase(Round round, Phase phase)
    {
        round.SetPhase(phase);
    }
    
    private void TimeoutAlices()
    	{
    		foreach (var round in Rounds.Where(x => !x.IsInputRegistrationEnded(x.Parameters.MaxInputCountByRound)).ToArray())
    		{
    			var alicesToRemove = round.Alices.Where(x => !x.ConfirmedConnection).ToArray();
    			foreach (var alice in alicesToRemove)
    			{
    				round.Alices.Remove(alice);
    			}
    
    			var removedAliceCount = alicesToRemove.Length;
    			if (removedAliceCount > 0)
    			{
    				round.LogInfo($"{removedAliceCount} alices timed out and removed.");
    			}
    		}
    	}
    
    private void TimeoutRounds()
    {
        foreach (var round in Rounds)
        {
            if (round.Phase == Phase.Ended) Rounds.Remove(round);
        }
    }
    
    private void AddRound(Round round)
    {
	    Rounds.Add(round);
    }
    
    internal void EndRound(Round round, EndRoundState endRoundState)
    {
	    round.EndRound(endRoundState);
    }
    
    // NOTE: Functions in Arena.Partial
    public InputRegistrationResponse RegisterInput(InputRegistrationRequest request)
    {
	    try
	    {
		    return RegisterInputCore(request);
	    }
	    catch (Exception ex) when (IsUserCheating(ex))
	    {
		    Logger.LogInfo($"{request.Input} is cheating: {ex.Message}");
		    _prison.CheatingDetected(request.Input, request.RoundId);
		    throw;
	    }
    }
    
    private InputRegistrationResponse RegisterInputCore(InputRegistrationRequest request)
	{
		MyCoin ?coin = OutpointToMyCoin(request);
		if (coin == null)
		{
			
		}
			var round = GetRound(request.RoundId);

			// Compute but don't commit updated coinjoin to round state, it will
			// be re-calculated on input confirmation. This is computed in here
			// for validation purposes.
			_ = round.Assert<ConstructionState>().AddInput(coin);

			CheckCoinIsNotBanned(coin.Outpoint, round);

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

			var alice = new Alice(coin, round, id);

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
			
			// NOTE: In the original code, this uses Task, so now the assignment later looks weird
			var amountCredential = round.AmountCredentialIssuer.HandleRequest(request.ZeroAmountCredentialRequests);
			var vsizeCredential = round.VsizeCredentialIssuer.HandleRequest(request.ZeroVsizeCredentialRequests);
			
			// TODO: Why is this checked after creating the tasks?
			if (round.RemainingInputVsizeAllocation < round.Parameters.MaxVsizeAllocationPerAlice)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.VsizeQuotaExceeded);
			}

			var commitAmountCredentialResponse = amountCredential;
			var commitVsizeCredentialResponse = vsizeCredential;

			round.Alices.Add(alice);

			return new(alice.Id,
				commitAmountCredentialResponse,
				commitVsizeCredentialResponse);
	}
	
	public void RemoveInput(InputsRemovalRequest request)
	{
		var round = GetRound(request.RoundId, Phase.InputRegistration);

		round.Alices.RemoveAll(x => x.Id == request.AliceId && x.ConfirmedConnection == false);
	}
    
	public void RegisterOutput(OutputRegistrationRequest request, CancellationToken cancellationToken)
	{
		return RegisterOutputCore(request, cancellationToken);
	}
	
	public EmptyResponse RegisterOutputCore(OutputRegistrationRequest request, CancellationToken cancellationToken)
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

		Bob bob = new(request.ScriptType, credentialAmount);

		var outputValue = bob.CalculateOutputAmount(round.Parameters.MiningFeeRate);

		var vsizeCredentialRequests = request.VsizeCredentialRequests;
		if (-vsizeCredentialRequests.Delta != bob.OutputVsize)
		{
			throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedVsizeCredentials, $"Round ({request.RoundId}): Incorrect requested vsize credentials.");
		}

		// Update the current round state with the additional output to ensure it's valid.
		var newState = round.AddOutput(new MyTxOut(outputValue, bob.ScriptType));
		
		// Verify the credential requests and prepare their responses.
		// TODO: These are not stored anywhere huh? I guess the client probably doesn't need a response and only checks 
		// the outputs in the final transwaction
		round.AmountCredentialIssuer.HandleRequest(request.AmountCredentialRequests);
		round.VsizeCredentialIssuer.HandleRequest(vsizeCredentialRequests);

		// Update round state.
		round.Bobs.Add(bob);
		round.CoinjoinState = newState;

		return EmptyResponse.Instance;
	}
    
    // NOTE: Rewritten OutpointToCoin.
    // In the original there are some exceptions thrown based on the number of confirmations. We assume that
    // the transaction has enough confirmations and hasn't been spent. The original function also returns the number
    // of confirmations, but that's not used later in the code, so we just return the MyCoin.
    public MyCoin? OutpointToMyCoin(InputRegistrationRequest request)
    {
	    MyTxOut? requestedTxOut = _transactionFactory.TryGetTxOut(request.Input);
	    if (requestedTxOut == null)
	    {
		    return null;
	    }

	    return new MyCoin(request.Input, requestedTxOut);
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
}