using NBitcoin;
using Soju.WabiSabi.Backend.Rounds;

namespace Soju.WabiSabi.Backend.Models;

public class WrongPhaseException : WabiSabiProtocolException
{
	public WrongPhaseException(Round round, params Phase[] expectedPhases)
		: base(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({round.Id}): Wrong phase ({round.Phase}).", exceptionData: new WrongPhaseExceptionData(round.Phase))
	{
		CurrentPhase = round.Phase;
		RoundId = round.Id;
		ExpectedPhases = expectedPhases;
	}

	public Phase CurrentPhase { get; }
	public Phase[] ExpectedPhases { get; }
	public uint256 RoundId { get; }
}