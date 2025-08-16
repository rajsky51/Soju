using NBitcoin;
using NBitcoin.RPC;

namespace Soju.BitcoinCore.Rpc;

public interface IRPCClient
{
	Network Network { get; }
	
	GetTxOutResponse? GetTxOut(uint256 txid, int index);
}
