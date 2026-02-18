using System.Threading;
using System.Threading.Tasks;
using Soju.WabiSabi.Models;

namespace Soju.WabiSabi.Coordinator.PostRequests;

public interface IWabiSabiApiRequestHandler
{
	InputRegistrationResponse RegisterInput(InputRegistrationRequest request);

	ConnectionConfirmationResponse ConfirmConnection(ConnectionConfirmationRequest request);

	EmptyResponse RegisterOutput(OutputRegistrationRequest request);

	void RemoveInput(InputsRemovalRequest request);

	void SignTransaction(TransactionSignaturesRequest request);

	RoundStateResponse GetStatus(RoundStateRequest request);
}
