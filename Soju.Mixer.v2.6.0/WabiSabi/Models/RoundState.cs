using NBitcoin;
using WabiSabi.Crypto;
using WabiSabi.Crypto.Randomness;
using Soju.WabiSabi.Coordinator.Rounds;
using Soju.WabiSabi.Crypto;
using Soju.WabiSabi.Models.MultipartyTransaction;
using CredentialIssuerParameters = WabiSabi.Crypto.CredentialIssuerParameters;

namespace Soju.WabiSabi.Models;

public record RoundState(uint256 Id,
	CredentialIssuerParameters AmountCredentialIssuerParameters,
	CredentialIssuerParameters VsizeCredentialIssuerParameters,
	Phase Phase,
	EndRoundState EndRoundState,
	DateTimeOffset InputRegistrationStart,
	MultipartyTransactionState CoinjoinState)
{
	private readonly Lazy<uint256> _calculatedRoundId = new(() => RoundHasher.CalculateHash(
		InputRegistrationStart,
		CoinjoinState.Parameters.AllowedInputAmounts,
		CoinjoinState.Parameters.AllowedInputTypes,
		CoinjoinState.Parameters.AllowedOutputAmounts,
		CoinjoinState.Parameters.AllowedOutputTypes,
		CoinjoinState.Parameters.Network,
		CoinjoinState.Parameters.MiningFeeRate.FeePerK,
		CoinjoinState.Parameters.MaxTransactionSize,
		CoinjoinState.Parameters.MinRelayTxFee.FeePerK,
		CoinjoinState.Parameters.MaxAmountCredentialValue,
		CoinjoinState.Parameters.MaxVsizeCredentialValue,
		CoinjoinState.Parameters.MaxVsizeAllocationPerAlice,
		CoinjoinState.Parameters.MaxSuggestedAmount,
		CoinjoinState.Parameters.CoordinationIdentifier,
		AmountCredentialIssuerParameters,
		VsizeCredentialIssuerParameters));

	public bool IsRoundIdMatching() => Id == _calculatedRoundId.Value;

	public static RoundState FromRound(Round round, int stateId = 0) =>
		new(
			round.Id,
			round.AmountCredentialIssuerParameters,
			round.VsizeCredentialIssuerParameters,
			round.Phase,
			round.EndRoundState,
			round.InputRegistrationStartTime,
			round.CoinjoinState.GetStateFrom(stateId)
			);

	public RoundState GetSubState(int skipFromBaseState) =>
		new(
			Id,
			AmountCredentialIssuerParameters,
			VsizeCredentialIssuerParameters,
			Phase,
			EndRoundState,
			InputRegistrationStart,
			CoinjoinState.GetStateFrom(skipFromBaseState)
			);

	public TState Assert<TState>() where TState : MultipartyTransactionState =>
		CoinjoinState switch
		{
			TState s => s,
			_ => throw new InvalidOperationException($"{typeof(TState).Name} state was expected but {CoinjoinState.GetType().Name} state was received.")
		};
}
