using System.Collections.Generic;
using NBitcoin;
using Soju.Blockchain.Keys;
using Soju.Crypto;

namespace Soju.WabiSabi.Client;

public interface IKeyChain
{
	// OwnershipProof GetOwnershipProof(IDestination destination, CoinJoinInputCommitmentData committedData);

	Transaction Sign(Transaction transaction, Coin coin, PrecomputedTransactionData precomputeTransactionData);
}
