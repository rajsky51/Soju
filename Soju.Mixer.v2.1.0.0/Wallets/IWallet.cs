using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.Transactions;
using Soju.Models;
using Soju.WabiSabi.Client;
using Soju.WabiSabi.Client.Batching;

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
	OutputProvider OutputProvider => new(DestinationProvider);
	PaymentBatch BatchedPayments => new();

	int AnonScoreTarget { get; }
	bool ConsolidationMode { get; set; }
	TimeSpan FeeRateMedianTimeFrame { get; }
	bool RedCoinIsolation { get; }
	CoinjoinSkipFactors CoinjoinSkipFactors { get; }

	bool IsWalletPrivate();

	IEnumerable<SmartCoin> GetCoinjoinCoinCandidates();

	IEnumerable<SmartTransaction> GetTransactions();
}