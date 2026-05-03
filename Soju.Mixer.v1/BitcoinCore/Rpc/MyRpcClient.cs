using NBitcoin;
using NBitcoin.RPC;
using System.Diagnostics;
using Soju.Extensions;
using Soju.Models;

namespace Soju.BitcoinCore.Rpc;

public class MyRpcClient : IRPCClient
{
	public record TransactionRecord(Transaction Tx, Height Height);
	
	public Network Network { get; }
	// NOTE: For now we only need to know the transactions, we don't need the number
	// of confirmations or anything else
	public Dictionary<uint256, TransactionRecord> TxRecords;
	public FeeRate CurrentMiningFeeRate;
	public Height CurrentFreeBlockHeight;
	
	public MyRpcClient (Network network)
	{
		Debug.Assert(network == Network.RegTest);
		Network = network;
		TxRecords = [];
		// NOTE: Absurdly big value, cannot be decimal.MaxValue
		CurrentMiningFeeRate = new FeeRate(1_000_000m);
		CurrentFreeBlockHeight = 0;
	}
	
	public uint256 SendRawTransaction(Transaction transaction)
	{
		uint256 txHash = transaction.GetHash();
		Debug.Assert(!TxRecords.ContainsKey(txHash));
		TxRecords.Add(txHash, new TransactionRecord(transaction, CurrentFreeBlockHeight));
		// TODO: Increasing block height after each transaction?
		CurrentFreeBlockHeight++;
		// NOTE: Let's return the hash, as in BitcoinFactory.GetMockMinimalRpc
		return txHash;
	}
	
	public GetTxOutResponse? GetTxOut(uint256 txid, int index)
	{
		if (TxRecords.TryGetValue(txid, out TransactionRecord record)) 
		{
			Transaction tx = record.Tx;
			if (index < tx.Outputs.Count) 
			{
				TxOut txout = tx.Outputs[index];
				return new GetTxOutResponse
				{
					Confirmations = CurrentFreeBlockHeight.Value - record.Height.Value,
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
	public Transaction GetRawTransaction(uint256 txid)
	{
		if (TxRecords.TryGetValue(txid, out TransactionRecord record))
		{
			return record.Tx;
		}
		Debug.Assert(false, "Queried for a non-existing transaction");
		return Transaction.Create(Network); // NOTE: Satisfying the compiler
	}
	
	public Height GetTransactionBlockHeight(uint256 txid)
	{
		if (TxRecords.TryGetValue(txid, out TransactionRecord record))
		{
			return record.Height;
		}
		Debug.Assert(false, "Queried for a non-existing transaction");
		return -1;
	}
	
	public void BumpHeight(int bumpValue)
	{
		CurrentFreeBlockHeight += bumpValue;
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

