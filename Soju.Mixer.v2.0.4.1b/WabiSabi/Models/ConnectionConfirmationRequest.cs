using NBitcoin;
using WabiSabi.CredentialRequesting;

namespace Soju.WabiSabi.Models;

public record ConnectionConfirmationRequest(
	uint256 RoundId,
	Guid AliceId,
	long RealAmountCredentialRequestDelta,
	long RealVsizeCredentialRequestDelta
);
