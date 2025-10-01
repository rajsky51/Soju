using NBitcoin;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.Transactions;
using Soju.Models;
using Soju.WabiSabi.Client;
using Soju.WabiSabi.Client.Batching;
using Soju.WabiSabi.Client.CoinJoin.Client.Decomposer;

namespace Soju.Wallets;

public interface IWallet
{
	string WalletName { get; }
	WalletId WalletId { get; }
	bool IsUnderPlebStop { get; }
	bool IsMixable { get; }

	/// <summary>
	/// Watch only wallets have no key chains.
	/// </summary>
	IKeyChain? KeyChain { get; }

	IDestinationProvider DestinationProvider { get; }
	IOutputProvider OutputProvider { get; }
	PaymentBatch BatchedPayments => new();

	int AnonScoreTarget { get; }
	bool ConsolidationMode { get; set; }
	TimeSpan FeeRateMedianTimeFrame { get; }
	bool RedCoinIsolation { get; }
	CoinjoinSkipFactors CoinjoinSkipFactors { get; }

	Task<bool> IsWalletPrivateAsync();

	Task<IEnumerable<SmartCoin>> GetCoinjoinCoinCandidatesAsync();

	Task<IEnumerable<SmartTransaction>> GetTransactionsAsync();
}
