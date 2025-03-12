using NBitcoin;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Soju.Wallets;

namespace Soju;

[DebuggerDisplay("{GetHash()}")]
public class DumbTransaction : IEquatable<DumbTransaction>
{
    public readonly uint256 Id;
    public bool IsWasabi2Cj;
    public int NInputs;
    public ConcurrentDictionary<WalletId, HashSet<DumbCoin>> Inputs;
    public int NOutputs;
    private readonly Lock _outputsLock = new();
    public ConcurrentDictionary<WalletId, HashSet<DumbCoin>> Outputs;

    public DumbTransaction(IDictionary<WalletId, HashSet<DumbCoin>>? inputs,
        IDictionary<WalletId, HashSet<DumbCoin>>? outputs, bool isWasabi2Cj = false)
    {
        Id = RandomUtils.GetUInt256();

        IsWasabi2Cj = isWasabi2Cj;

        if (inputs is not null) Inputs = new ConcurrentDictionary<WalletId, HashSet<DumbCoin>>(inputs);
        else Inputs = new ConcurrentDictionary<WalletId, HashSet<DumbCoin>>();

        if (outputs is not null) Outputs = new ConcurrentDictionary<WalletId, HashSet<DumbCoin>>(outputs);
        else Outputs = new ConcurrentDictionary<WalletId, HashSet<DumbCoin>>();
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
