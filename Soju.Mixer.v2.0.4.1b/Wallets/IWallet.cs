using System.Collections.Generic;
using System.Threading.Tasks;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.Transactions;
using Soju.WabiSabi.Client;

namespace Soju.Wallets;

public interface IWallet
{
	string WalletName { get; }
	bool IsUnderPlebStop { get; }
	bool IsMixable { get; }

	/// <summary>
	/// Watch only wallets have no key chains.
	/// </summary>
	IKeyChain? KeyChain { get; }

	IDestinationProvider DestinationProvider { get; }
	int AnonScoreTarget { get; }
	bool ConsolidationMode { get; }
	TimeSpan FeeRateMedianTimeFrame { get; }
	bool RedCoinIsolation { get; }

	bool IsWalletPrivate();

	IEnumerable<SmartCoin> GetCoinjoinCoinCandidates();

	IEnumerable<SmartTransaction> GetTransactions();
}
