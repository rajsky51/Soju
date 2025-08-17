using WabiSabi.CredentialRequesting;

namespace Soju.WabiSabi.Models;

public record ConnectionConfirmationResponse(
	CredentialsResponse ZeroAmountCredentials,
	CredentialsResponse ZeroVsizeCredentials,
	CredentialsResponse? RealAmountCredentials = null,
	CredentialsResponse? RealVsizeCredentials = null
);