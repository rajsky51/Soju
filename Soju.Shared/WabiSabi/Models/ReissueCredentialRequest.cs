using NBitcoin;
using WabiSabi.CredentialRequesting;

namespace Soju.WabiSabi.Models;

public record ReissueCredentialRequest(
    uint256 RoundId,
    RealCredentialsRequest RealAmountCredentialRequests,
    RealCredentialsRequest RealVsizeCredentialRequests,
    ZeroCredentialsRequest ZeroAmountCredentialRequests,
    ZeroCredentialsRequest ZeroVsizeCredentialsRequests
);