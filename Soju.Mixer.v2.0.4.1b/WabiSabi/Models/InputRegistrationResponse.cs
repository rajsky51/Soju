using Newtonsoft.Json;

namespace Soju.WabiSabi.Models;

public record InputRegistrationResponse(
	Guid AliceId,
	[property: JsonProperty("isPayingZeroCoordinationFee")] bool IsCoordinationFeeExempted
);
