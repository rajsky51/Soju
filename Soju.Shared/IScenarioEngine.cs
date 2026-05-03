namespace Soju;

public interface IScenarioEngine
{
	void CreateAndAddWallet(string name, decimal anonScoreTarget, bool redCoinIsolation);
	void AddWalletFund(string walletName, long satoshis);
	MixingResult MixRound(decimal miningFeeRateSatoshisPerByte);
}

public record WalletInitData
(
	string Name,
	decimal AnonScoreTarget,
	bool RedCoinIsolation
);
