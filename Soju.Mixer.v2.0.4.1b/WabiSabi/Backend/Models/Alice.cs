using NBitcoin;
using Soju.Extensions;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Models;

namespace Soju.WabiSabi.Backend.Models;

public class Alice
{
	public Alice(Coin coin, Round round, Guid id, bool isCoordinationFeeExempted)
	{
		// TODO init syntax?
		Round = round;
		Coin = coin;
		Id = id;
		IsCoordinationFeeExempted = isCoordinationFeeExempted;
	}

	public Round Round { get; }
	public Guid Id { get; }
	public Coin Coin { get; }
	public Money TotalInputAmount => Coin.Amount;
	public int TotalInputVsize => Coin.ScriptPubKey.EstimateInputVsize();

	public bool ConfirmedConnection { get; set; } = false;
	public bool ReadyToSign { get; set; }
	public bool IsCoordinationFeeExempted { get; } = false;

	public long CalculateRemainingVsizeCredentials(int maxRegistrableSize) => maxRegistrableSize - TotalInputVsize;

	public Money CalculateRemainingAmountCredentials(FeeRate feeRate, CoordinationFeeRate coordinationFeeRate) =>
		Coin.EffectiveValue(feeRate, IsCoordinationFeeExempted ? CoordinationFeeRate.Zero : coordinationFeeRate);
}
