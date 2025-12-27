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
		SmartCoin coin)
	{
		var roundParameters = roundState.CoinjoinState.Parameters;
		AliceId = aliceId;
		RoundId = roundState.Id;
		SmartCoin = coin;
		FeeRate = roundParameters.MiningFeeRate;
		MaxVsizeAllocationPerAlice = roundParameters.MaxVsizeAllocationPerAlice;
	}

	public Guid AliceId { get; }
	public uint256 RoundId { get; }
	public SmartCoin SmartCoin { get; }
	public readonly FeeRate FeeRate;
	public long IssuedAmountCredentialsValue { get; set; }
	public long IssuedVsizeCredentialsValue { get; set; }
	public readonly long MaxVsizeAllocationPerAlice;

	public Money EffectiveValue => SmartCoin.EffectiveValue(FeeRate);
}
