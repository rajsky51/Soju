using Newtonsoft.Json;
using WabiSabi.CredentialRequesting;

namespace Soju.WabiSabi.Models;

public record InputRegistrationResponse(
	Guid AliceId,
	CredentialsResponse AmountCredentials,
	CredentialsResponse VsizeCredentials
);