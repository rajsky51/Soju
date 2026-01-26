using NBitcoin;
using WabiSabi.CredentialRequesting;

namespace Soju.WabiSabi.Models;

public record InputRegistrationRequest(
	uint256 RoundId,
	OutPoint Input
);
