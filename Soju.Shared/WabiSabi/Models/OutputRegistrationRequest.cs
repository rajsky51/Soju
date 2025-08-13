using NBitcoin;
using WabiSabi.CredentialRequesting;

namespace Soju.WabiSabi.Models;

public record OutputRegistrationRequest(
    uint256 RoundId,
    ScriptType ScriptType,
    RealCredentialsRequest AmountCredentialRequests,
    RealCredentialsRequest VsizeCredentialRequests
);