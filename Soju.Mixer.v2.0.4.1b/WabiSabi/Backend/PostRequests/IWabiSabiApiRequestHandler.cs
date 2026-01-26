using System.Threading;
using System.Threading.Tasks;
using Soju.WabiSabi.Models;

namespace Soju.WabiSabi.Backend.PostRequests;

public interface IWabiSabiApiRequestHandler
{
	InputRegistrationResponse RegisterInput(InputRegistrationRequest request);

	ConnectionConfirmationResponse ConfirmConnection(ConnectionConfirmationRequest request);

	EmptyResponse RegisterOutput(OutputRegistrationRequest request);

	void RemoveInput(InputsRemovalRequest request);

	void SignTransaction(TransactionSignaturesRequest request);
	
	// TODO: nocheckin Remove from other implementations
	// ReissueCredentialResponse Reissuance(ReissueCredentialRequest request);

	RoundStateResponse GetStatus(RoundStateRequest request);
	
	// TODO: nocheckin Remove from other implementations
	// void ReadyToSign(ReadyToSignRequestRequest request);
}
