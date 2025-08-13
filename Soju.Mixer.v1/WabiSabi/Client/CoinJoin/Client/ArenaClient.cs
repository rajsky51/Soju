using NBitcoin;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WabiSabi.Crypto;
using WabiSabi.Crypto.ZeroKnowledge;
using Soju.Crypto;
using Soju.Helpers;
using Soju.WabiSabi.Backend.PostRequests;
using Soju.WabiSabi.Models;
using Soju.WabiSabi.Models.MultipartyTransaction;

namespace Soju.WabiSabi.Client.CoinJoin.Client;

public class ArenaClient
{
	public ArenaClient(
		WabiSabiClient amountCredentialClient,
		WabiSabiClient vsizeCredentialClient,
		string coordinatorIdentifier,
		IWabiSabiApiRequestHandler requestHandler)
	{
		AmountCredentialClient = amountCredentialClient;
		VsizeCredentialClient = vsizeCredentialClient;
		CoordinatorIdentifier = coordinatorIdentifier;
		RequestHandler = requestHandler;
	}

	public WabiSabiClient AmountCredentialClient { get; }
	public WabiSabiClient VsizeCredentialClient { get; }
	public string CoordinatorIdentifier { get; }
	public IWabiSabiApiRequestHandler RequestHandler { get; }

	public ArenaResponse<Guid> RegisterInput(
		uint256 roundId,
		OutPoint outPoint)
	{
		var zeroAmountCredentialRequestData = AmountCredentialClient.CreateRequestForZeroAmount();
		var zeroVsizeCredentialRequestData = VsizeCredentialClient.CreateRequestForZeroAmount();

		var inputRegistrationResponse = RequestHandler.RegisterInput(
			new InputRegistrationRequest(
				roundId,
				outPoint,
				zeroAmountCredentialRequestData.CredentialsRequest,
				zeroVsizeCredentialRequestData.CredentialsRequest));

		var realAmountCredentials = AmountCredentialClient.HandleResponse(inputRegistrationResponse.AmountCredentials, zeroAmountCredentialRequestData.CredentialsResponseValidation);
		var realVsizeCredentials = VsizeCredentialClient.HandleResponse(inputRegistrationResponse.VsizeCredentials, zeroVsizeCredentialRequestData.CredentialsResponseValidation);

		return new(inputRegistrationResponse.AliceId, realAmountCredentials, realVsizeCredentials);
	}

	public void RemoveInput(uint256 roundId, Guid aliceId)
	{
		RequestHandler.RemoveInput(new InputsRemovalRequest(roundId, aliceId));
	}

	public void RegisterOutput(
		uint256 roundId,
		ScriptType scriptPubKeyType,
		IEnumerable<Credential> amountCredentialsToPresent,
		IEnumerable<Credential> vsizeCredentialsToPresent,
		CancellationToken cancellationToken)
	{
		Guard.InRange(nameof(amountCredentialsToPresent), amountCredentialsToPresent, 0, AmountCredentialClient.NumberOfCredentials);
		Guard.InRange(nameof(vsizeCredentialsToPresent), vsizeCredentialsToPresent, 0, VsizeCredentialClient.NumberOfCredentials);

		var presentedAmount = amountCredentialsToPresent.Sum(x => x.Value);
		var (realAmountCredentialRequest, realAmountCredentialResponseValidation) = AmountCredentialClient.CreateRequest(
			amountCredentialsToPresent,
			cancellationToken);

		var presentedVsize = vsizeCredentialsToPresent.Sum(x => x.Value);
		var (realVsizeCredentialRequest, realVsizeCredentialResponseValidation) = VsizeCredentialClient.CreateRequest(
			vsizeCredentialsToPresent,
			cancellationToken);

		RequestHandler.RegisterOutput(
			new OutputRegistrationRequest(
				roundId,
				scriptPubKeyType,
				realAmountCredentialRequest,
				realVsizeCredentialRequest));
	}

	public ArenaResponse ReissueCredential(
		uint256 roundId,
		IEnumerable<long> amountsToRequest,
		IEnumerable<long> vsizesToRequest,
		IEnumerable<Credential> amountCredentialsToPresent,
		IEnumerable<Credential> vsizeCredentialsToPresent,
		CancellationToken cancellationToken)
	{
		Guard.InRange(nameof(amountCredentialsToPresent), amountCredentialsToPresent, 0, AmountCredentialClient.NumberOfCredentials);

		var presentedAmount = amountCredentialsToPresent.Sum(x => x.Value);
		if (amountsToRequest.Sum() != presentedAmount)
		{
			throw new InvalidOperationException($"Reissuance amounts sum must equal the sum of the presented ones.");
		}

		var presentedVsize = vsizeCredentialsToPresent.Sum(x => x.Value);
		if (vsizesToRequest.Sum() > presentedVsize)
		{
			throw new InvalidOperationException($"Reissuance vsizes sum can not be greater than the sum of the presented ones.");
		}

		var (realVsizeCredentialRequest, realVsizeCredentialResponseValidation) = VsizeCredentialClient.CreateRequest(
			vsizesToRequest,
			vsizeCredentialsToPresent,
			cancellationToken);

		var (realAmountCredentialRequest, realAmountCredentialResponseValidation) = AmountCredentialClient.CreateRequest(
			amountsToRequest,
			amountCredentialsToPresent,
			cancellationToken);

		var zeroAmountCredentialRequestData = AmountCredentialClient.CreateRequestForZeroAmount();
		var zeroVsizeCredentialRequestData = VsizeCredentialClient.CreateRequestForZeroAmount();

		var reissuanceResponse = RequestHandler.Reissuance(
			new ReissueCredentialRequest(
				roundId,
				realAmountCredentialRequest,
				realVsizeCredentialRequest,
				zeroAmountCredentialRequestData.CredentialsRequest,
				zeroVsizeCredentialRequestData.CredentialsRequest));

		var realAmountCredentials = AmountCredentialClient.HandleResponse(reissuanceResponse.RealAmountCredentials, realAmountCredentialResponseValidation);
		var realVsizeCredentials = VsizeCredentialClient.HandleResponse(reissuanceResponse.RealVsizeCredentials, realVsizeCredentialResponseValidation);
		var zeroAmountCredentials = AmountCredentialClient.HandleResponse(reissuanceResponse.ZeroAmountCredentials, zeroAmountCredentialRequestData.CredentialsResponseValidation);
		var zeroVsizeCredentials = VsizeCredentialClient.HandleResponse(reissuanceResponse.ZeroVsizeCredentials, zeroVsizeCredentialRequestData.CredentialsResponseValidation);

		return new(realAmountCredentials.Concat(zeroAmountCredentials), realVsizeCredentials.Concat(zeroVsizeCredentials));
	}

	public ArenaResponse<bool> ConfirmConnection(
		uint256 roundId,
		Guid aliceId,
		IEnumerable<long> amountsToRequest,
		IEnumerable<long> vsizesToRequest,
		IEnumerable<Credential> amountCredentialsToPresent,
		IEnumerable<Credential> vsizeCredentialsToPresent)
	{
		Guard.InRange(nameof(amountsToRequest), amountsToRequest, 1, ProtocolConstants.CredentialNumber);
		Guard.InRange(nameof(amountCredentialsToPresent), amountCredentialsToPresent, 0, ProtocolConstants.CredentialNumber);
		Guard.InRange(nameof(vsizeCredentialsToPresent), vsizeCredentialsToPresent, 0, ProtocolConstants.CredentialNumber);
		Guard.InRange(nameof(vsizesToRequest), vsizesToRequest, 1, VsizeCredentialClient.NumberOfCredentials);

		var realAmountCredentialRequestData = AmountCredentialClient.CreateRequest(
			amountsToRequest,
			amountCredentialsToPresent);

		var realVsizeCredentialRequestData = VsizeCredentialClient.CreateRequest(
			vsizesToRequest,
			vsizeCredentialsToPresent);

		var zeroAmountCredentialRequestData = AmountCredentialClient.CreateRequestForZeroAmount();
		var zeroVsizeCredentialRequestData = VsizeCredentialClient.CreateRequestForZeroAmount();

		var confirmConnectionResponse = RequestHandler.ConfirmConnection(
			new ConnectionConfirmationRequest(
				roundId,
				aliceId,
				zeroAmountCredentialRequestData.CredentialsRequest,
				realAmountCredentialRequestData.CredentialsRequest,
				zeroVsizeCredentialRequestData.CredentialsRequest,
				realVsizeCredentialRequestData.CredentialsRequest));

		var zeroAmountCredentials = AmountCredentialClient.HandleResponse(confirmConnectionResponse.ZeroAmountCredentials, zeroAmountCredentialRequestData.CredentialsResponseValidation);
		var zeroVsizeCredentials = VsizeCredentialClient.HandleResponse(confirmConnectionResponse.ZeroVsizeCredentials, zeroVsizeCredentialRequestData.CredentialsResponseValidation);

		if (confirmConnectionResponse is { RealAmountCredentials: { }, RealVsizeCredentials: { } })
		{
			var realAmountCredentials = AmountCredentialClient.HandleResponse(confirmConnectionResponse.RealAmountCredentials, realAmountCredentialRequestData.CredentialsResponseValidation);
			var realVsizeCredentials = VsizeCredentialClient.HandleResponse(confirmConnectionResponse.RealVsizeCredentials, realVsizeCredentialRequestData.CredentialsResponseValidation);
			return new(true, realAmountCredentials.Concat(zeroAmountCredentials), realVsizeCredentials.Concat(zeroVsizeCredentials));
		}

		return new(false, zeroAmountCredentials, zeroVsizeCredentials);
	}

	public async Task SignTransactionAsync(
		uint256 roundId,
		Coin coin,
		IKeyChain keyChain, // unused now
		TransactionWithPrecomputedData unsignedCoinJoin,
		CancellationToken cancellationToken)
	{
		var signedCoinJoin = keyChain.Sign(unsignedCoinJoin.Transaction, coin, unsignedCoinJoin.PrecomputedTransactionData);
		var txInput = signedCoinJoin.Inputs.AsIndexedInputs().First(input => input.PrevOut == coin.Outpoint);
		if (!txInput.VerifyScript(coin, ScriptVerify.Standard, unsignedCoinJoin.PrecomputedTransactionData, out var error))
		{
			throw new InvalidOperationException($"Witness is missing. Reason {nameof(ScriptError)} code: {error}.");
		}

		await RequestHandler.SignTransactionAsync(new TransactionSignaturesRequest(roundId, txInput.Index, txInput.WitScript), cancellationToken).ConfigureAwait(false);
	}

	public RoundStateResponse GetStatusAsync(RoundStateRequest request)
	{
		return RequestHandler.GetStatus(request);
	}

	public void ReadyToSignAsync(
		uint256 roundId,
		Guid aliceId)
	{
		RequestHandler.ReadyToSign(
			new ReadyToSignRequestRequest(roundId, aliceId));
	}
}
