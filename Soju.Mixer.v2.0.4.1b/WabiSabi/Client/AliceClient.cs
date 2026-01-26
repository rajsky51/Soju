using NBitcoin;
using Soju.Blockchain.TransactionOutputs;
using Soju.Extensions;
using Soju.WabiSabi.Models;

namespace Soju.WabiSabi.Client;

public class AliceClient
{
	public AliceClient(
		Guid aliceId,
		RoundState roundState,
		SmartCoin coin,
		bool isCoordinationFeeExempted)
	{
		var roundParameters = roundState.CoinjoinState.Parameters;
		AliceId = aliceId;
		RoundId = roundState.Id;
		SmartCoin = coin;
		FeeRate = roundParameters.MiningFeeRate;
		CoordinationFeeRate = roundParameters.CoordinationFeeRate;
		MaxVsizeAllocationPerAlice = roundParameters.MaxVsizeAllocationPerAlice;
		IsCoordinationFeeExempted = isCoordinationFeeExempted;
	}

	public Guid AliceId { get; }
	public uint256 RoundId { get; }
	public SmartCoin SmartCoin { get; }
	private FeeRate FeeRate { get; }
	private CoordinationFeeRate CoordinationFeeRate { get; }
	public long IssuedAmountCredentialsValue { get; set; }
	public long IssuedVsizeCredentialsValue { get; set; }
	public long MaxVsizeAllocationPerAlice { get; }
	public bool IsCoordinationFeeExempted { get; }

	public Money EffectiveValue => SmartCoin.EffectiveValue(FeeRate, IsCoordinationFeeExempted ? CoordinationFeeRate.Zero : CoordinationFeeRate);
}
