namespace Soju.WabiSabi.Client.CoinJoin.Manager.StatusChangedEvents;

public enum CoinjoinError
{
    NoCoinsEligibleToMix,
    AutoConjoinDisabled,
    UserInSendWorkflow,
    NotEnoughUnprivateBalance,
    BackendNotSynchronized,
    AllCoinsPrivate,
    UserWasntInRound,
    NoConfirmedCoinsEligibleToMix,
    CoinsRejected,
    OnlyImmatureCoinsAvailable,
    OnlyExcludedCoinsAvailable,
    UneconomicalRound,
    RandomlySkippedRound,
    MiningFeeRateTooHigh,
    MinInputCountTooLow
}