using Soju.WabiSabi.Coordinator.Rounds;

namespace Soju.WabiSabi.Coordinator.Models;

public record WrongPhaseExceptionData(Phase CurrentPhase) : ExceptionData;
