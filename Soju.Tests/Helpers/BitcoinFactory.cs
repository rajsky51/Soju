using NBitcoin;
using System.Diagnostics;
using Soju.Blockchain.Analysis;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.Transactions;
using Soju.Helpers;
using Soju.Wallets;

namespace Soju.Tests.Helpers;

public static class BitcoinFactory
{
    public static DumbTransaction CreateDumbTransaction(
        int othersInputCount = 1,
        int othersOutputCount = 1,
        int ownInputCount = 0,
        int ownOutputCount = 0,
        WalletId? ownWalletId = null,
        bool orderByAmount = false)
    {
        return CreateDumbTransaction(
            othersInputCount,
            Enumerable.Repeat(Money.Coins(1m), othersOutputCount).ToArray(),
            Enumerable.Repeat((Money.Coins(1.1m), 1), ownInputCount).ToArray(),
            Enumerable.Repeat((Money.Coins(1m), 1), ownOutputCount).ToArray(),
            ownWalletId,
            orderByAmount);
    }

    // Doesn't calculate fees
    public static DumbTransaction CreateDumbTransaction(
        int othersInputCount,
        IReadOnlyCollection<Money> othersOutputs,
        IReadOnlyCollection<(Money value, int anonset)> ownInputs,
        IReadOnlyCollection<(Money value, int anonset)> ownOutputs,
        WalletId? ownWalletId,
        bool orderByAmount)
    {
        ownWalletId ??= new WalletId(Guid.NewGuid());
        
        DumbTransaction tx = new();

        Money remainingSumOtherOutputs = othersOutputs.Sum();

        for (int i = 0; i < othersInputCount; i++)
        {
            DumbTransaction tmpTx = new();
            Money coinAmount = Money.Satoshis(Random.Shared.NextInt64(0, remainingSumOtherOutputs.Satoshi / 2));
            // The last coin pays the remaining output sum
            if (i == othersInputCount - 1) coinAmount = remainingSumOtherOutputs;
            DumbCoin newCoin = tmpTx.AddOutputCoin(coinAmount, ScriptType.P2WPKH, 1, new WalletId(Guid.NewGuid()));
            // Set the index the same way as in the original helper, we're faking it anyway
            newCoin.Index = (uint)Random.Shared.Next(0, 100);
            tx.TryAddInput(newCoin);
        }

        Debug.Assert(tx.Inputs.SelectMany(kvp => kvp.Value).TotalAmount() >= othersOutputs.Sum());

        foreach (var input in ownInputs)
        {
            DumbTransaction tmpTx = new();
            DumbCoin newCoin = tmpTx.AddOutputCoin(input.value, ScriptType.P2WPKH, input.anonset, ownWalletId);
            tx.TryAddInput(newCoin);
        }

        // The wallet ids won't correspond to any of the input ones, but that shouldn't matter
        foreach (Money outputAmount in othersOutputs)
        {
            tx.AddOutputCoin(outputAmount, ScriptType.P2WPKH, 1, new WalletId(Guid.NewGuid()));
        }

        foreach (var output in ownOutputs)
        {
            tx.AddOutputCoin(output.value, ScriptType.P2WPKH, output.anonset, ownWalletId);
        }

        if (orderByAmount) tx.OrderOutputsByAmountDescending();
        
        return tx;
    }

    public static DumbCoin CreateDumbCoin(WalletId ownWalletId, Money amount, bool confirmed = true, int anonymitySet = 1)
    {
        
    }
    
    public static SmartCoin CreateSmartCoin(Transaction tx, HdPubKey pubKey, Money amount, bool confirmed = true, int anonymitySet = 1)
    {
        var height = confirmed ? new Height(CryptoHelpers.RandomInt(0, 200)) : Height.Mempool;
        pubKey.SetKeyState(KeyState.Used);
        tx.Outputs.Add(new TxOut(amount, pubKey.GetAssumedScriptPubKey()));
        tx.Inputs.Add(CreateOutPoint());
        var stx = new SmartTransaction(tx, height);
        pubKey.SetAnonymitySet(anonymitySet, stx.GetHash());
        var sc = new SmartCoin(stx, (uint)tx.Outputs.Count - 1, pubKey);
        BlockchainAnalyzer.SetIsSufficientlyDistancedFromExternalKeys(sc);
        return sc;
    }
    
    public static OutPoint CreateOutPoint()
        => new(CreateUint256(), (uint)CryptoHelpers.RandomInt(0, 100));
    
    public static uint256 CreateUint256()
    {
        var rand = new UnsecureRandom();
        var bytes = new byte[32];
        rand.GetBytes(bytes);
        return new uint256(bytes);
    }
}