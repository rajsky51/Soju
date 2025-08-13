using Soju.WabiSabi.Backend.Rounds;

namespace Soju.WabiSabi.Backend.Models;

public record WrongPhaseExceptionData(Phase CurrentPhase) : ExceptionData;