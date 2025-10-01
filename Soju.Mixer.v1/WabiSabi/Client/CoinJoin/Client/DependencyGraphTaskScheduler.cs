using NBitcoin;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WabiSabi.Crypto.ZeroKnowledge;
using Soju.Blockchain.Keys;
using Soju.Crypto;
using Soju.Helpers;
using Soju.Logging;
using Soju.WabiSabi.Backend.Models;
using Soju.WabiSabi.Client.CredentialDependencies;

namespace Soju.WabiSabi.Client.CoinJoin.Client;

public static class DependencyGraphTaskScheduler
{
	public record OutputRegistrationError();
	public record UnknownError(Script ScriptPubKey) : OutputRegistrationError;
	public record AlreadyRegisteredScriptError(Script ScriptPubKey) : OutputRegistrationError;

	public static IEnumerable<(AliceClient AliceClient, InputNode Node)> PairAliceClientAndRequestNodes(IEnumerable<AliceClient> aliceClients, DependencyGraph graph)
	{
		var inputNodes = graph.GetInputs();

		if (aliceClients.Count() != inputNodes.Count())
		{
			throw new InvalidOperationException("_graph vs Alice inputs mismatch");
		}

		return aliceClients.Zip(inputNodes);
	}
}
