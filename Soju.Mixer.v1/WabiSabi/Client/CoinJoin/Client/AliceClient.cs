using NBitcoin;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Soju.Crypto;
using Soju.Logging;
using Soju.WabiSabi.Backend.Models;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Models;
using Soju.Blockchain.TransactionOutputs;
using Soju.WabiSabi.Client.RoundStateAwaiters;
using Soju.Extensions;
using System.Net.Http;
using WabiSabi.Crypto.ZeroKnowledge;
using Soju.WabiSabi.Models.MultipartyTransaction;

namespace Soju.WabiSabi.Client.CoinJoin.Client;

public class AliceClient
{
	public AliceClient(
		Guid aliceId,
		RoundState roundState,
		ArenaClient arenaClient,
		SmartCoin coin,
		IEnumerable<Credential> issuedAmountCredentials,
		IEnumerable<Credential> issuedVsizeCredentials)
	{
		var roundParameters = roundState.CoinjoinState.Parameters;
		AliceId = aliceId;
		RoundId = roundState.Id;
		ArenaClient = arenaClient;
		SmartCoin = coin;
		FeeRate = roundParameters.MiningFeeRate;
		IssuedAmountCredentials = issuedAmountCredentials;
		IssuedVsizeCredentials = issuedVsizeCredentials;
		MaxVsizeAllocationPerAlice = roundParameters.MaxVsizeAllocationPerAlice;
	}

	public Guid AliceId { get; }
	public uint256 RoundId { get; }
	public readonly ArenaClient ArenaClient;
	public SmartCoin SmartCoin { get; }
	public readonly FeeRate FeeRate;
	public IEnumerable<Credential> IssuedAmountCredentials { get; set; }
	public IEnumerable<Credential> IssuedVsizeCredentials { get; set; }
	public readonly long MaxVsizeAllocationPerAlice;

	public Money EffectiveValue => SmartCoin.EffectiveValue(FeeRate);
}
