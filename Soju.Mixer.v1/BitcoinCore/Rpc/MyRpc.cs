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
	public FeeRate CurrentMiningFeeRate;
	
	public MyRpc (Network network)
	{
		Debug.Assert(network == Network.RegTest);
		Network = network;
		Transactions = new();
		// NOTE: Absurdly big value, cannot be decimal.MaxValue
		CurrentMiningFeeRate = new FeeRate(1_000_000m); 
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
	
	// NOTE: Should only ever be used if we know the transaction exists
	public Transaction GetRawTransaction(uint256 txid, bool throwIfNotFound = true)
	{
		if (Transactions.TryGetValue(txid, out Transaction tx))
		{
			return tx;
		}
		Debug.Assert(false, "Queried for a non-existing transaction");
		return tx; // NOTE: Satisfying the compiler
	}
	
	public FeeRate GetCurrentMiningFeeRate()
	{
		return CurrentMiningFeeRate;
	}
	
	public void SetCurrentMiningFeeRate(FeeRate feeRate)
	{
		CurrentMiningFeeRate = feeRate;
	}
}

