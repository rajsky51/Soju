using NBitcoin;
using WabiSabi.CredentialRequesting;

namespace Soju.WabiSabi.Models;

public record ConnectionConfirmationRequest(
	uint256 RoundId,
	Guid AliceId,
	ZeroCredentialsRequest ZeroAmountCredentialRequests,
	RealCredentialsRequest RealAmountCredentialRequests,
	ZeroCredentialsRequest ZeroVsizeCredentialRequests,
	RealCredentialsRequest RealVsizeCredentialRequests
);