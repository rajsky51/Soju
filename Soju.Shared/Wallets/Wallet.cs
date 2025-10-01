using Microsoft.Extensions.Hosting;
using NBitcoin;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Soju.Backend.Models;
using Soju.Blockchain.Analysis.Clustering;
using Soju.Blockchain.Analysis.FeesEstimation;
using Soju.Blockchain.BlockFilters;
using Soju.Blockchain.Blocks;
using Soju.Blockchain.Keys;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.TransactionProcessing;
using Soju.Blockchain.Transactions;
using Soju.Extensions;
using Soju.Helpers;
using Soju.Logging;
using Soju.Models;
using Soju.Services;
using Soju.Stores;
using Soju.Userfacing;
using Soju.WabiSabi.Client;
using Soju.WabiSabi.Client.Batching;
using Soju.WebClients.Wasabi;

namespace Soju.Wallets;

public class Wallet : IWallet
{
	public Wallet(
		string dataDir,
		Network network,
		KeyManager keyManager,
		BitcoinStore bitcoinStore,
		WasabiSynchronizer syncer,
		ServiceConfiguration serviceConfiguration,
		HybridFeeProvider feeProvider,
		TransactionProcessor transactionProcessor,
		WalletFilterProcessor walletFilterProcessor,
		CpfpInfoProvider? cpfpInfoProvider)
	{
		Guard.NotNullOrEmptyOrWhitespace(nameof(dataDir), dataDir);
		Network = network;
		KeyManager = keyManager;

		DestinationProvider = new InternalDestinationProvider(KeyManager);

		TransactionProcessor = transactionProcessor;
		Coins = TransactionProcessor.Coins;
		WalletFilterProcessor = walletFilterProcessor;
		BatchedPayments = new PaymentBatch();
		OutputProvider = new PaymentAwareOutputProvider(DestinationProvider, BatchedPayments);
		WalletId = new WalletId(Guid.NewGuid());
	}

	public WalletId WalletId { get; }

	public KeyManager KeyManager { get; }
	public string WalletName => KeyManager.WalletName;

	public CoinsRegistry Coins { get; }

	public bool RedCoinIsolation => KeyManager.RedCoinIsolation;
	public CoinjoinSkipFactors CoinjoinSkipFactors => KeyManager.CoinjoinSkipFactors;

	public Network Network { get; }

	public bool IsLoggedIn { get; private set; }
	public string Password { get; set; }

	public IKeyChain? KeyChain { get; private set; }

	public IDestinationProvider DestinationProvider { get; }

	public IOutputProvider OutputProvider { get; }
	public PaymentBatch BatchedPayments { get; }

	public int AnonScoreTarget => KeyManager.AnonScoreTarget;
	public bool ConsolidationMode { get; set; }

	public bool IsMixable => true;
		// State == WalletState.Started // Only running wallets
		// && !KeyManager.IsWatchOnly; // that are not watch-only wallets

	public TimeSpan FeeRateMedianTimeFrame => TimeSpan.FromHours(KeyManager.FeeRateMedianTimeFrameHours);

	public bool IsUnderPlebStop => Coins.TotalAmount() <= KeyManager.PlebStopThreshold;

	public ICoinsView GetAllCoins() => Coins.AsAllCoinsView();

	public Task<bool> IsWalletPrivateAsync() => Task.FromResult(IsWalletPrivate());

	public bool IsWalletPrivate() => GetPrivacyPercentage() >= 100;

	public Task<IEnumerable<SmartTransaction>> GetTransactionsAsync() => Task.FromResult(GetTransactions());

	public IEnumerable<SmartCoin> GetCoinjoinCoinCandidates() => Coins;

	/// <summary>
	/// Get all the transactions associated to the wallet ordered by blockchain.
	/// </summary>
	public IEnumerable<SmartTransaction> GetTransactions()
	{
		var walletTransactions = new HashSet<SmartTransaction>();

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

	/// <summary>
	/// Gets the wallet transaction with the given txid, if the transaction exists.
	/// </summary>
	public bool TryGetTransaction(uint256 txid, [NotNullWhen(true)] out SmartTransaction? smartTransaction)
	{
		// The lock is necessary to make sure that coins registry and transaction store do not change in this code block.
		// The assumption is that the transaction processor is the only component modifying coins registry and transaction store.
		lock (TransactionProcessor.Lock)
		{
			smartTransaction = null;
			bool isKnown = Coins.IsKnown(txid);

			if (isKnown && !BitcoinStore.TransactionStore.TryGetTransaction(txid, out smartTransaction))
			{
				throw new UnreachableException($"{nameof(Coins)} and {nameof(BitcoinStore.TransactionStore)} are not in sync (txid '{txid}').");
			}

			return isKnown;
		}
	}

	public HdPubKey GetNextReceiveAddress(IEnumerable<string> destinationLabels, ScriptPubKeyType scriptPubKeyType)
	{
		return KeyManager.GetNextReceiveKey(new LabelsArray(destinationLabels), scriptPubKeyType);
	}

	public int GetPrivacyPercentage()
	{
		var currentPrivacyScore = Coins.Sum(x => x.Amount.Satoshi * Math.Min(x.HdPubKey.AnonymitySet - 1, x.IsPrivate(AnonScoreTarget) ? AnonScoreTarget - 1 : AnonScoreTarget - 2));
		var maxPrivacyScore = Coins.TotalAmount().Satoshi * (AnonScoreTarget - 1);
		int pcPrivate = maxPrivacyScore == 0M ? 0 : (int)(currentPrivacyScore * 100 / maxPrivacyScore);

		return pcPrivate;
	}

	public bool TryLogin(string password, out string? compatibilityPasswordUsed)
	{
		compatibilityPasswordUsed = null;

		if (KeyManager.IsWatchOnly)
		{
			IsLoggedIn = true;
			Password = "";
		}
		else if (PasswordHelper.TryPassword(KeyManager, password, out compatibilityPasswordUsed))
		{
			IsLoggedIn = true;
			Password = compatibilityPasswordUsed ?? Guard.Correct(password);
			KeyChain = new KeyChain(KeyManager, Password);
		}

		return IsLoggedIn;
	}

	public void Logout()
	{
		IsLoggedIn = false;
	}

	public void Initialize()
	{
		if (State > WalletState.WaitingForInit)
		{
			throw new InvalidOperationException($"{nameof(State)} must be {WalletState.Uninitialized} or {WalletState.WaitingForInit}. Current state: {State}.");
		}

		try
		{
			KeyManager.AssertNetworkOrClearBlockState(Network);
			EnsureHeightsAreAtLeastSegWitActivation();

			TransactionProcessor.WalletRelevantTransactionProcessed += TransactionProcessor_WalletRelevantTransactionProcessed;
			BitcoinStore.MempoolService.TransactionReceived += Mempool_TransactionReceived;

			State = WalletState.Initialized;
		}
		catch
		{
			State = WalletState.Uninitialized;
			throw;
		}
	}

	/// <inheritdoc/>
	public override async Task StartAsync(CancellationToken cancel)
	{
		if (State != WalletState.Initialized)
		{
			throw new InvalidOperationException($"{nameof(State)} must be {WalletState.Initialized}. Current state: {State}.");
		}

		try
		{
			State = WalletState.Starting;

			await RuntimeParams.LoadAsync().ConfigureAwait(false);

			await WalletFilterProcessor.StartAsync(cancel).ConfigureAwait(false);

			await LoadWalletStateAsync(cancel).ConfigureAwait(false);
			await LoadDummyMempoolAsync().ConfigureAwait(false);
			LoadExcludedCoins();

			await base.StartAsync(cancel).ConfigureAwait(false);

			State = WalletState.Started;
		}
		catch
		{
			State = WalletState.Initialized;
			throw;
		}
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

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Perform final synchronization in the background.
		if (KeyManager.UseTurboSync)
		{
			await PerformSynchronizationAsync(SyncType.NonTurbo, stoppingToken).ConfigureAwait(false);
		}

		Logger.LogInfo($"Wallet '{WalletName}' is fully synchronized.");
	}

	public string AddCoinJoinPayment(IDestination destination, Money amount)
	{
		var paymentId = BatchedPayments.AddPayment(destination, amount);
		return paymentId.ToString();
	}

	/// <inheritdoc/>
	public override async Task StopAsync(CancellationToken cancel)
	{
		try
		{
			var prevState = State;
			State = WalletState.Stopping;

			if (prevState < WalletState.Stopping)
			{
				await base.StopAsync(cancel).ConfigureAwait(false);

				if (prevState >= WalletState.Initialized)
				{
					await WalletFilterProcessor.StopAsync(cancel).ConfigureAwait(false);
					WalletFilterProcessor.Dispose();

					BitcoinStore.IndexStore.NewFilters -= IndexDownloader_NewFiltersAsync;
					BitcoinStore.MempoolService.TransactionReceived -= Mempool_TransactionReceived;
					TransactionProcessor.WalletRelevantTransactionProcessed -= TransactionProcessor_WalletRelevantTransactionProcessed;
				}
			}
		}
		finally
		{
			State = WalletState.Stopped;
		}
	}

	private void TransactionProcessor_WalletRelevantTransactionProcessed(object? sender, ProcessedResult e)
	{
		try
		{
			WalletRelevantTransactionProcessed?.Invoke(this, e);


			if (CpfpInfoProvider.ShouldRequest(e.Transaction))
			{
				CpfpInfoProvider?.ScheduleRequest(e.Transaction);
			}
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
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

	private async void IndexDownloader_NewFiltersAsync(object? sender, IEnumerable<FilterModel> filters)
	{
		try
		{
			var filterModels = filters as FilterModel[] ?? filters.ToArray();

			if (KeyManager.UseTurboSync)
			{
				await WalletFilterProcessor.ProcessAsync(new List<SyncType> { SyncType.Turbo, SyncType.NonTurbo }, CancellationToken.None).ConfigureAwait(false);
			}
			else
			{
				await WalletFilterProcessor.ProcessAsync(SyncType.Complete, CancellationToken.None).ConfigureAwait(false);
			}

			NewFiltersProcessed?.Invoke(this, filterModels);
			await Task.Delay(100).ConfigureAwait(false);

			// Make sure fully synced and this filter is the latest filter.
			if (BitcoinStore.SmartHeaderChain.HashesLeft != 0 || BitcoinStore.SmartHeaderChain.TipHash != filterModels.Last().Header.BlockHash)
			{
				return;
			}

			if (CpfpInfoProvider is not null)
			{
				await CpfpInfoProvider.UpdateCacheAsync(CancellationToken.None).ConfigureAwait(false);
			}

			await BitcoinStore.MempoolService.TryPerformMempoolCleanupAsync(Synchronizer.HttpClient).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// Cancellation token kicked in while processing the new filters, don't log anything.
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex);
		}
	}

	private async Task LoadWalletStateAsync(CancellationToken cancel)
	{
		// Make sure that the keys are asserted in case of an empty HdPubKeys array.
		KeyManager.GetKeys();

		Height bestTurboSyncHeight = KeyManager.GetBestHeight(SyncType.Turbo);

		TransactionProcessor.Process(BitcoinStore.TransactionStore.ConfirmedStore.GetTransactions().TakeWhile(x => x.Height <= bestTurboSyncHeight));

		BitcoinStore.IndexStore.NewFilters += IndexDownloader_NewFiltersAsync;

		// Each time a new batch of filters is downloaded, request a synchronization.
		var lastHashesLeft = BitcoinStore.SmartHeaderChain.HashesLeft;
		while (BitcoinStore.SmartHeaderChain.HashesLeft > 0)
		{
			cancel.ThrowIfCancellationRequested();
			if (lastHashesLeft == BitcoinStore.SmartHeaderChain.HashesLeft)
			{
				await Task.Delay(100, cancel).ConfigureAwait(false);
				continue;
			}
			lastHashesLeft = BitcoinStore.SmartHeaderChain.HashesLeft;
			await PerformSynchronizationAsync(KeyManager.UseTurboSync ? SyncType.Turbo : SyncType.Complete, cancel).ConfigureAwait(false);
		}

		// Request a synchronization once all filters were downloaded.
		await PerformSynchronizationAsync(KeyManager.UseTurboSync ? SyncType.Turbo : SyncType.Complete, cancel).ConfigureAwait(false);
	}

	public async Task PerformSynchronizationAsync(SyncType syncType, CancellationToken cancellationToken)
	{
		await WalletFilterProcessor.ProcessAsync(syncType, cancellationToken).ConfigureAwait(false);
	}

	private async Task LoadDummyMempoolAsync()
	{
		if (BitcoinStore.TransactionStore.MempoolStore.IsEmpty())
		{
			return;
		}

		// Only clean the mempool if we're fully synchronized.
		if (BitcoinStore.SmartHeaderChain.HashesLeft == 0)
		{
			try
			{
				var client = new WasabiClient(Synchronizer.HttpClient);
				var compactness = 10;

				var mempoolHashes = await client.GetMempoolHashesAsync(compactness).ConfigureAwait(false);

				var txsToProcess = new List<SmartTransaction>();
				foreach (var tx in BitcoinStore.TransactionStore.MempoolStore.GetTransactions())
				{
					uint256 txid = tx.GetHash();
					if (mempoolHashes.Contains(txid.ToString()[..compactness]))
					{
						txsToProcess.Add(tx);
						Logger.LogInfo($"'{WalletName}': Transaction was successfully tested against the backend's mempool hashes: {txid}.");
					}
					else
					{
						BitcoinStore.TransactionStore.MempoolStore.TryRemove(txid, out _);
					}
				}

				TransactionProcessor.Process(txsToProcess);
			}
			catch (Exception ex)
			{
				// When there's a connection failure do not clean the transactions, add them to processing.
				TransactionProcessor.Process(BitcoinStore.TransactionStore.MempoolStore.GetTransactions());

				Logger.LogWarning(ex);
			}
		}
		else
		{
			TransactionProcessor.Process(BitcoinStore.TransactionStore.MempoolStore.GetTransactions());
		}
	}

	public void SetWaitingForInitState()
	{
		if (State != WalletState.Uninitialized)
		{
			throw new InvalidOperationException($"{nameof(State)} must be {WalletState.Uninitialized}. Current state: {State}.");
		}

		State = WalletState.WaitingForInit;
	}

	public void ExcludeCoinFromCoinJoin(OutPoint outpoint, bool exclude = true)
	{
		if (!Coins.TryGetByOutPoint(outpoint, out var coin))
		{
			throw new InvalidOperationException($"Coin '{outpoint}' doesn't belong to the wallet or is spent.");
		}

		coin.IsExcludedFromCoinJoin = exclude;
		UpdateExcludedCoinFromCoinJoin();
	}

	public void UpdateExcludedCoinsFromCoinJoin(OutPoint[] outPointsToExclude)
	{
		foreach (var coin in Coins)
		{
			coin.IsExcludedFromCoinJoin = outPointsToExclude.Contains(coin.Outpoint);
		}

		UpdateExcludedCoinFromCoinJoin();
	}

	private void UpdateExcludedCoinFromCoinJoin()
	{
		var excludedOutpoints = Coins.Where(c => c.IsExcludedFromCoinJoin).Select(c => c.Outpoint);
		KeyManager.SetExcludedCoinsFromCoinJoin(excludedOutpoints);
	}

	public void UpdateUsedHdPubKeysLabels(Dictionary<HdPubKey, LabelsArray> hdPubKeysWithLabels)
	{
		if (hdPubKeysWithLabels.Count == 0)
		{
			return;
		}

		foreach (var item in hdPubKeysWithLabels)
		{
			item.Key.SetLabel(item.Value);
		}

		KeyManager.ToFile();
	}

	private void EnsureHeightsAreAtLeastSegWitActivation()
	{
		var startingSegwitHeight = new Height(SmartHeader.GetStartingHeader(Network).Height);
		if (startingSegwitHeight > KeyManager.GetBestHeight(SyncType.Complete))
		{
			KeyManager.SetBestHeight(startingSegwitHeight);
		}

		if (startingSegwitHeight > KeyManager.GetBestHeight(SyncType.Turbo))
		{
			KeyManager.SetBestTurboSyncHeight(startingSegwitHeight);
		}
	}
}
