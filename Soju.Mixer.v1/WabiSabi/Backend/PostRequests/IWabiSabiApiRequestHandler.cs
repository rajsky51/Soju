using System.Threading;
using System.Threading.Tasks;
using Soju.WabiSabi.Models;

namespace Soju.WabiSabi.Backend.PostRequests;

public interface IWabiSabiApiRequestHandler
{
    InputRegistrationResponse RegisterInput(InputRegistrationRequest request);

    ConnectionConfirmationResponse ConfirmConnection(ConnectionConfirmationRequest request);

    void RegisterOutput(OutputRegistrationRequest request);

    void RemoveInput(InputsRemovalRequest request);

    void SignTransaction(TransactionSignaturesRequest request);

    ReissueCredentialResponse Reissuance(ReissueCredentialRequest request);

    RoundStateResponse GetStatus(RoundStateRequest request);

    void ReadyToSign(ReadyToSignRequestRequest request);
}