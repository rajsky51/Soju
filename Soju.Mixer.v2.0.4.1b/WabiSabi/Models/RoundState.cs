using NBitcoin;
using WabiSabi.Crypto;
using WabiSabi.Crypto.Randomness;
using Soju.WabiSabi.Backend.Rounds;
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

	public WabiSabiClient CreateAmountCredentialClient(WasabiRandom random) =>
		new(AmountCredentialIssuerParameters, random, CoinjoinState.Parameters.MaxAmountCredentialValue);

	public WabiSabiClient CreateVsizeCredentialClient(WasabiRandom random) =>
		new(VsizeCredentialIssuerParameters, random, CoinjoinState.Parameters.MaxVsizeCredentialValue);
}
