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
		string coordinatorIdentifier)
	{
		AmountCredentialClient = amountCredentialClient;
		VsizeCredentialClient = vsizeCredentialClient;
		CoordinatorIdentifier = coordinatorIdentifier;
	}

	public WabiSabiClient AmountCredentialClient { get; }
	public WabiSabiClient VsizeCredentialClient { get; }
	public string CoordinatorIdentifier { get; }
}
