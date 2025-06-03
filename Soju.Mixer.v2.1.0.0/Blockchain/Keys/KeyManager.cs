using NBitcoin;

namespace Soju.Blockchain.Keys;

public class KeyManager
{
    public const int DefaultAnonScoreTarget = 5;
    public const bool DefaultAutoCoinjoin = false;
    public const bool DefaultRedCoinIsolation = false;
    public const int DefaultFeeRateMedianTimeFrameHours = 0;

    public const int AbsoluteMinGapLimit = 21;
    public const int MaxGapLimit = 10_000;
    public static readonly Money DefaultPlebStopThreshold = Money.Coins(0.01m);
}