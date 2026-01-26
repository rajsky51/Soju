using Microsoft.Extensions.Hosting;
using NBitcoin;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Soju.Blockchain.Analysis.Clustering;
using Soju.Blockchain.Analysis.FeesEstimation;
using Soju.Blockchain.Keys;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.TransactionProcessing;
using Soju.Blockchain.Transactions;
using Soju.Extensions;
using Soju.Helpers;
using Soju.Logging;
using Soju.Models;
using Soju.WabiSabi.Client;

namespace Soju.Wallets;

public class Wallet : IWallet
{
	// NOTE: In the original code the TransactionProcessor and Coins creation is 
	// in RegisterServices
	public Wallet(Network network, KeyManager keyManager, AllTransactionStore txStore, Money dustThreshold)
	{
		Network = Guard.NotNull(nameof(network), network);
		KeyManager = Guard.NotNull(nameof(keyManager), keyManager);
		if (!KeyManager.IsWatchOnly)
		{
			KeyChain = new KeyChain(KeyManager, Kitchen);
		}
		
		DestinationProvider = new InternalDestinationProvider(KeyManager);
		
		TransactionProcessor = new TransactionProcessor(txStore, KeyManager, dustThreshold);
		Coins = TransactionProcessor.Coins;
	}

	public KeyManager KeyManager { get; }
	public string WalletName => KeyManager.WalletName;

	/// <summary>Unspent Transaction Outputs</summary>
	public ICoinsView Coins { get; private set; }

	public bool RedCoinIsolation => KeyManager.RedCoinIsolation;

	public Network Network { get; }
	public TransactionProcessor TransactionProcessor { get; private set; }

	public Kitchen Kitchen { get; } = new();

	public IKeyChain? KeyChain { get; }

	public IDestinationProvider DestinationProvider { get; }

	public int AnonScoreTarget => KeyManager.AnonScoreTarget;
	public bool ConsolidationMode => false;

	public bool IsMixable => true;
		// State == WalletState.Started // Only running wallets
		// && !KeyManager.IsWatchOnly // that are not watch-only wallets
		// && Kitchen.HasIngredients;

	public TimeSpan FeeRateMedianTimeFrame => TimeSpan.FromHours(KeyManager.FeeRateMedianTimeFrameHours);

	public bool IsUnderPlebStop => Coins.TotalAmount() <= KeyManager.PlebStopThreshold;

	public ICoinsView GetAllCoins() => ((CoinsRegistry)Coins).AsAllCoinsView();

	public bool IsWalletPrivate() => GetPrivacyPercentage(new CoinsView(Coins), AnonScoreTarget) >= 1;

	public IEnumerable<SmartCoin> GetCoinjoinCoinCandidates() => Coins;

	public IEnumerable<SmartTransaction> GetTransactions()
	{
		var walletTransactions = new List<SmartTransaction>();
		foreach (SmartCoin coin in GetAllCoins())
		{
			walletTransactions.Add(coin.Transaction);
			if (coin.SpenderTransaction is not null)
			{
				walletTransactions.Add(coin.SpenderTransaction);
			}
		}
		return walletTransactions.OrderByBlockchain().ToList();
	}

	public HdPubKey GetNextReceiveAddress(IEnumerable<string> destinationLabels)
	{
		return KeyManager.GetNextReceiveKey(new LabelsArray(destinationLabels));
	}

	private double GetPrivacyPercentage(CoinsView coins, int privateThreshold)
	{
		var privateAmount = coins.FilterBy(x => x.IsPrivate(privateThreshold)).TotalAmount();
		var normalAmount = coins.FilterBy(x => !x.IsPrivate(privateThreshold)).TotalAmount();

		var privateDecimalAmount = privateAmount.ToDecimal(MoneyUnit.BTC);
		var normalDecimalAmount = normalAmount.ToDecimal(MoneyUnit.BTC);
		var totalDecimalAmount = privateDecimalAmount + normalDecimalAmount;

		var pcPrivate = totalDecimalAmount == 0M ? 1d : (double)(privateDecimalAmount / totalDecimalAmount);
		return pcPrivate;
	}

	private void LoadExcludedCoins()
	{
		bool isUpdateRequired = false;
		foreach (var excludedCoin in KeyManager.ExcludedCoinsFromCoinJoin)
		{
			var coin = Coins.SingleOrDefault(c => c.Outpoint == excludedCoin);
			if (coin != null)
			{
				coin.IsExcludedFromCoinJoin = true;
			}
			else
			{
				isUpdateRequired = true;
			}
		}
		if (isUpdateRequired)
		{
			UpdateExcludedCoinFromCoinJoin();
		}
	}

	private void Mempool_TransactionReceived(object? sender, SmartTransaction tx)
	{
		try
		{
			if (!TransactionProcessor.IsAware(tx.GetHash()))
			{
				TransactionProcessor.Process(tx);
			}
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex);
		}
	}

	public void UpdateExcludedCoinFromCoinJoin()
	{
		var excludedOutpoints = Coins.Where(c => c.IsExcludedFromCoinJoin).Select(c => c.Outpoint);
		KeyManager.SetExcludedCoinsFromCoinJoin(excludedOutpoints);
	}

	public void UpdateUsedHdPubKeysLabels(Dictionary<HdPubKey, LabelsArray> hdPubKeysWithLabels)
	{
		if (!hdPubKeysWithLabels.Any())
		{
			return;
		}

		foreach (var item in hdPubKeysWithLabels)
		{
			item.Key.SetLabel(item.Value);
		}

		KeyManager.ToFile();
	}
}
