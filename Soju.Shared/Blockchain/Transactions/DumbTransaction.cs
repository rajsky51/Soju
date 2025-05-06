using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using NBitcoin;
using Soju.Blockchain.Analysis;
using Soju.Blockchain.TransactionOutputs;
using Soju.Wallets;

namespace Soju.Blockchain.Transactions;

[DebuggerDisplay("{GetHash()}")]
public class DumbTransaction : IEquatable<DumbTransaction>
{
    private Lazy<long[]> _outputValues;
    private Lazy<bool> _isWasabi2Cj;
    
    public readonly uint256 Id;
    public bool IsWasabi2Cj => _isWasabi2Cj.Value;
    public long[] OutputValues => _outputValues.Value;
    
    public int NInputs;
    public ConcurrentDictionary<WalletId, HashSet<DumbCoin>> Inputs;
    
    private readonly Lock _outputsLock = new();
    public int NOutputs;
    public ConcurrentDictionary<WalletId, HashSet<DumbCoin>> Outputs;

    public DumbTransaction(IDictionary<WalletId, HashSet<DumbCoin>>? inputs,
        IDictionary<WalletId, HashSet<DumbCoin>>? outputs)
    {
        Id = RandomUtils.GetUInt256();

        if (inputs is not null) Inputs = new ConcurrentDictionary<WalletId, HashSet<DumbCoin>>(inputs);
        else Inputs = new ConcurrentDictionary<WalletId, HashSet<DumbCoin>>();
        NInputs = Inputs.SelectMany(kvp => kvp.Value).Count();

        if (outputs is not null) Outputs = new ConcurrentDictionary<WalletId, HashSet<DumbCoin>>(outputs);
        else Outputs = new ConcurrentDictionary<WalletId, HashSet<DumbCoin>>();
        NOutputs = Outputs.SelectMany(kvp => kvp.Value).Count();

        _outputValues = new Lazy<long[]>(() => Outputs.SelectMany(kvp => kvp.Value).OrderBy(coin => coin.Index).Select(coin => coin.Amount.Satoshi).ToArray(), true);
        _isWasabi2Cj = new Lazy<bool>(
            () => NOutputs >= 2 // Sanity check.
            && NInputs >= 50 // 50 was the minimum input count at the beginning of Wasabi 2.
            && OutputValues.Count(x => StandardDenominationsProvider.StdDenoms.Contains(x)) > OutputValues.Length * 0.8 // Most of the outputs contains the denomination.
            && OutputValues.Zip(OutputValues.Skip(1)).All(p => p.First >= p.Second), // Outputs are ordered descending.
            isThreadSafe: true);
    }

    public DumbTransaction() : this(null, null) {}

    public bool TryAddInput(DumbCoin input)
    {
        if (!Inputs.TryGetValue(input.WalletId, out var coins)) 
        {
            coins = [];
            Inputs[input.WalletId] = coins;
        }
        
        if (coins.Add(input))
        {
            Interlocked.Increment(ref NInputs);
            return true;
        }
        else
        {
            return false;
        }
    }

    public DumbCoin AddOutputCoin(Money amount, ScriptType scriptType, double anonymitySet, WalletId walletId)
    {
        DumbCoin newCoin;
        lock (_outputsLock)
        {
            newCoin = new DumbCoin(this, amount, scriptType, anonymitySet, (uint)NOutputs, walletId);
            NOutputs++;
        }
        // NOTE: Here's a possible race condition when two threads will add outputs to the same wallet (but that shouldn't happen)
        if (!Outputs.TryGetValue(walletId, out var coins)) 
        {
            coins = [];
            Outputs[walletId] = coins;
        }
        coins.Add(newCoin);
        
        return newCoin;
    }

    public void RemoveWalletInputs(WalletId walletId)
    {
        Inputs.Remove(walletId, out var inputCoins);
        if (inputCoins is not null) Interlocked.Add(ref NInputs, -inputCoins.Count());
    }

    public void OrderOutputsByAmountDescending()
    {
        DumbCoin[] sortedOutputs = Outputs.SelectMany(kvp => kvp.Value).OrderByDescending(coin => coin.Amount).ToArray();
        for (uint i = 0; i < sortedOutputs.Length; i++)
        {
            sortedOutputs[i].Index = i;
        }
    }

    public int EstimateVSize()
    {
        return Inputs.SelectMany(kvp => kvp.Value).Sum(coin => coin.ScriptType.EstimateInputVsize())
            + Outputs.SelectMany(kvp => kvp.Value).Sum(coin => coin.ScriptType.EstimateOutputVsize());
    }

    public uint256 GetHash() => Id;

    public override int GetHashCode() => GetHash().GetHashCode();

    public override bool Equals(object? obj) => Equals(obj as DumbTransaction);

    public bool Equals(DumbTransaction? other) => this == other;

    public static bool operator ==(DumbTransaction? x, DumbTransaction? y) 
    {
        if (x is null && y is null) return true;
        if (x is null || y is null) return false;

        if (x.GetHashCode() != y.GetHashCode()) return false;

        if (x.Id          != y.Id ||
            x.IsWasabi2Cj != y.IsWasabi2Cj ||
            x.Inputs      != y.Inputs ||
            x.Outputs     != y.Outputs)
        {
            return false;
        }
        
        return true;
    }

    public static bool operator !=(DumbTransaction? x, DumbTransaction? y) 
    {
        return !(x == y);
    }

    public override string ToString()
    {
        StringBuilder sb = new();

        sb.Append($"id: {Id}\n");
        sb.Append($"is Wasabi 2 Coinjoin: {(IsWasabi2Cj ? "yes" : "no")}\n");
        if (!Inputs.IsEmpty) 
        {
            sb.Append("Inputs\n");
            foreach (var walletInput in Inputs)
            {
                sb.Append($"wallet id: {walletInput.Key}\n");
                foreach (var input in walletInput.Value) 
                {
                    sb.Append($"{input}\n");
                }
            }
        }
        if (!Outputs.IsEmpty)
        {
            sb.Append("Outputs\n");
            foreach (var walletOutput in Outputs)
            {
                sb.Append($"wallet id: {walletOutput.Key}\n");
                foreach (var output in walletOutput.Value) 
                {
                    sb.Append($"{output}\n");
                }
            }
        }

        return sb.ToString();
    }
}
