using NBitcoin;
using System.Diagnostics;
using WabiSabi.Crypto;
using WabiSabi.Crypto.Randomness;
using Soju.WabiSabi.Backend.Models;
using Soju.WabiSabi.Crypto;
using Soju.WabiSabi.Models.MultipartyTransaction;

namespace Soju.WabiSabi.Backend.Rounds;

/// <summary>
/// DO ONLY APPEND TO THE END
/// Otherwise serialization ruins compatibility with clients.
/// Do not insert, do not delete, do not reorder, only append!
/// </summary>
public enum EndRoundState
{
	None,
	AbortedWithError,
	AbortedNotEnoughAlices,
	TransactionBroadcastFailed,
	TransactionBroadcasted,
	NotAllAlicesSign,
	AbortedNotEnoughAlicesSigned,
	AbortedNotAllAlicesConfirmed,
	AbortedLoadBalancing,
	AbortedDoubleSpendingDetected = AbortedNotAllAlicesConfirmed
}

public class Round
{
	private Lazy<uint256> _id;
	public Round(RoundParameters parameters, WasabiRandom random)
	{
		Parameters = parameters;

		CoinjoinState = new ConstructionState(Parameters);

		AmountCredentialIssuer = new(new(random), random, Parameters.MaxAmountCredentialValue);
		VsizeCredentialIssuer = new(new(random), random, Parameters.MaxVsizeCredentialValue);
		AmountCredentialIssuerParameters = AmountCredentialIssuer.CredentialIssuerSecretKey.ComputeCredentialIssuerParameters();
		VsizeCredentialIssuerParameters = VsizeCredentialIssuer.CredentialIssuerSecretKey.ComputeCredentialIssuerParameters();
		
		InputRegistrationStartTime = DateTime.UtcNow;

		_id = new Lazy<uint256>(CalculateHash);
	}

	public uint256 Id => _id.Value;
	public MultipartyTransactionState CoinjoinState { get; set; }

	public CredentialIssuer AmountCredentialIssuer { get; }
	public CredentialIssuer VsizeCredentialIssuer { get; }
	public CredentialIssuerParameters AmountCredentialIssuerParameters { get; }
	public CredentialIssuerParameters VsizeCredentialIssuerParameters { get; }
	public List<Alice> Alices { get; } = new();
	public int InputCount => Alices.Count;
	public List<Bob> Bobs { get; } = new();

	public Phase Phase { get; private set; } = Phase.InputRegistration;
	// NOTE: My addition; represents StartTime of InputRegistrationTimeFrame (used so there's some variable data for RoundId calculation)
	// TODO: IMPORTANT: Change to DateTimeOffset in all versions
	public DateTime InputRegistrationStartTime;
	public DateTimeOffset End { get; private set; }
	public EndRoundState EndRoundState { get; set; }
	public int RemainingInputVsizeAllocation => Parameters.InitialInputVsizeAllocation - (InputCount * Parameters.MaxVsizeAllocationPerAlice);

	public bool FastSigningPhase { get; set; }

	public RoundParameters Parameters { get; }
	public Script CoordinatorScript { get; set; }

	public TState Assert<TState>() where TState : MultipartyTransactionState =>
		CoinjoinState switch
		{
			TState s => s,
			_ => throw new InvalidOperationException($"{typeof(TState).Name} state was expected but {CoinjoinState.GetType().Name} state was received.")
		};

	public void SetPhase(Phase phase)
	{
		if (!Enum.IsDefined(phase))
		{
			throw new ArgumentException($"Invalid phase {phase}. This is a bug.", nameof(phase));
		}

		this.LogInfo($"Phase changed: {Phase} -> {phase}");
		Phase = phase;

		if (phase == Phase.Ended)
		{
			End = DateTimeOffset.UtcNow;
		}
	}

	public void EndRound(EndRoundState finalState)
	{
		PublishWitnessesIfPossible();
		SetPhase(Phase.Ended);
		EndRoundState = finalState;
	}

	public virtual bool IsInputRegistrationEnded(int maxInputCount)
	{
		if (Phase > Phase.InputRegistration)
		{
			return true;
		}

		if (InputCount >= maxInputCount)
		{
			return true;
		}
		
		return false;
	}

	public ConstructionState AddInput(Coin coin)
		=> Assert<ConstructionState>().AddInput(coin);

	public ConstructionState AddOutput(TxOut output)
		=> Assert<ConstructionState>().AddOutput(output);

	public SigningState AddWitness(int index, WitScript witness)
		=> Assert<SigningState>().AddWitness(index, witness);

	private uint256 CalculateHash()
		=> RoundHasher.CalculateHash(
				InputRegistrationStartTime,
				Parameters.AllowedInputAmounts,
				Parameters.AllowedInputTypes,
				Parameters.AllowedOutputAmounts,
				Parameters.AllowedOutputTypes,
				Parameters.Network,
				Parameters.MiningFeeRate.FeePerK,
				Parameters.MaxTransactionSize,
				Parameters.MinRelayTxFee.FeePerK,
				Parameters.MaxAmountCredentialValue,
				Parameters.MaxVsizeCredentialValue,
				Parameters.MaxVsizeAllocationPerAlice,
				Parameters.MaxSuggestedAmount,
				Parameters.CoordinationIdentifier,
				AmountCredentialIssuerParameters,
				VsizeCredentialIssuerParameters);

	private void PublishWitnessesIfPossible()
	{
		if (CoinjoinState is SigningState signingState)
		{
			CoinjoinState = signingState.PublishWitnesses();
		}
	}
}
