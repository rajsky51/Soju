using NBitcoin;
using Soju.WabiSabi.Backend.Rounds;
using Soju.MyNBitcoin;

namespace Soju.WabiSabi.Backend.Models;

public class Alice
{
    public Alice(MyCoin coin, Round round, Guid id)
    {
        Round = round;
        Coin = coin;
        Id = id;
    }

    public Round Round { get; }
    public Guid Id { get; }
    public DateTimeOffset Deadline { get; set; } = DateTimeOffset.UtcNow;
    public MyCoin Coin { get; }
    public Money TotalInputAmount => Coin.Amount;
    public int TotalInputVsize => Coin.ScriptPubKeyType.EstimateInputVsize();

    public bool ConfirmedConnection { get; set; } = false;
    public bool ReadyToSign { get; set; }

    public long CalculateRemainingVsizeCredentials(int maxRegistrableSize) => maxRegistrableSize - TotalInputVsize;

    public Money CalculateRemainingAmountCredentials(FeeRate feeRate) =>
        Coin.EffectiveValue(feeRate);

    public void SetDeadlineRelativeTo(TimeSpan connectionConfirmationTimeout)
    {
        // Have alice timeouts a bit sooner than the timeout of connection confirmation phase.
        Deadline = DateTimeOffset.UtcNow + (connectionConfirmationTimeout * 0.9);
    }
}