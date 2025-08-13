using NBitcoin;

namespace Soju.WabiSabi.Models;

public record InputsRemovalRequest(
    uint256 RoundId,
    Guid AliceId
);