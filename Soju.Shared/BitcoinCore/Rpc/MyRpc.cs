using NBitcoin;
using NBitcoin.RPC;
using System.Diagnostics;
using Soju.Extensions;

namespace Soju.BitcoinCore.Rpc;

public class MyRpc : IRPCClient
{
	public Network Network { get; }
	// NOTE: For now we only need to know the transactions, we don't need the number
	// of confirmations or anything else
	public Dictionary<uint256, Transaction> Transactions;
	
	public MyRpc (Network network)
	{
		Debug.Assert(network == Network.RegTest);
		Network = network;
		Transactions = new();
	}
	
	public uint256 SendRawTransaction(Transaction transaction)
	{
		uint256 txHash = transaction.GetHash();
		Debug.Assert(!Transactions.ContainsKey(txHash));
		Transactions.Add(txHash, transaction);
		// NOTE: Output is never used, but let's return the hash. As in BitcoinFactory.GetMockMinimalRpc
		return txHash;
	}
	
	public GetTxOutResponse? GetTxOut(uint256 txid, int index)
	{
		if (Transactions.TryGetValue(txid, out Transaction tx)) 
		{
			if (index < tx.Outputs.Count) 
			{
				TxOut txout = tx.Outputs[index];
				// TODO: Rework
				return new GetTxOutResponse
				{
					Confirmations = 100,
					BestBlock = uint256.Zero,
					TxOut = txout,
					IsCoinBase = false,
					ScriptPubKeyType = txout.ScriptPubKey.GetScriptType().ToString()
				};
			}
		}
		return null;
	}
}

