using NBitcoin;
using Soju.Crypto;
using Soju.Extensions;
using Soju.WabiSabi.Backend.Rounds;

namespace Soju.WabiSabi.Backend.Models;

public class Alice
{
	public Alice(Coin coin, OwnershipProof ownershipProof, Round round, Guid id)
	{
		// TODO init syntax?
		Round = round;
		Coin = coin;
		OwnershipProof = ownershipProof;
		Id = id;
	}

	public Round Round { get; }
	public Guid Id { get; }
	public Coin Coin { get; }
	public OwnershipProof OwnershipProof { get; }
	public Money TotalInputAmount => Coin.Amount;
	public int TotalInputVsize => Coin.ScriptPubKey.EstimateInputVsize();

	public bool ConfirmedConnection { get; set; } = false;
	public bool ReadyToSign { get; set; }

	public long CalculateRemainingVsizeCredentials(int maxRegistrableSize) => maxRegistrableSize - TotalInputVsize;

	public Money CalculateRemainingAmountCredentials(FeeRate feeRate) =>
		Coin.EffectiveValue(feeRate);
}