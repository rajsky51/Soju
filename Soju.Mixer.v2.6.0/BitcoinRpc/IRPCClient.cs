using NBitcoin;
using NBitcoin.RPC;

namespace Soju.BitcoinRpc;

public interface IRPCClient
{
	Network Network { get; }
	
	uint256 SendRawTransaction(Transaction transaction);
	
	GetTxOutResponse? GetTxOut(uint256 txid, int index);
	
	Transaction GetRawTransaction(uint256 txid, bool throwIfNotFound = true);
	
	FeeRate GetCurrentMiningFeeRate();
	void SetCurrentMiningFeeRate(FeeRate feeRate);
}