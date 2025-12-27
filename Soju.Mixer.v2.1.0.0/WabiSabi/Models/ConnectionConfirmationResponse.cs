using WabiSabi.CredentialRequesting;

namespace Soju.WabiSabi.Models;

public record ConnectionConfirmationResponse(
	long RealAmountCredentials,
	long RealVsizeCredentials
);