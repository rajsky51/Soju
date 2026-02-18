using NBitcoin;
using Soju.Extensions;
using Soju.WabiSabi.Coordinator.Rounds;

namespace Soju.WabiSabi.Coordinator.Models;

public class Alice
{
	public Alice(Coin coin, Round round, Guid id)
	{
		// TODO init syntax?
		Round = round;
		Coin = coin;
		Id = id;
	}

	public Round Round { get; }
	public Guid Id { get; }
	public Coin Coin { get; }
	public Money TotalInputAmount => Coin.Amount;
	public int TotalInputVsize => Coin.ScriptPubKey.EstimateInputVsize();

	public bool ConfirmedConnection { get; set; } = false;
	public bool ReadyToSign { get; set; }

	public long CalculateRemainingVsizeCredentials(int maxRegistrableSize) => maxRegistrableSize - TotalInputVsize;

	public Money CalculateRemainingAmountCredentials(FeeRate feeRate) =>
		Coin.EffectiveValue(feeRate);
}
