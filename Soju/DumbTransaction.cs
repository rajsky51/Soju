using NBitcoin;
using NBitcoin.Crypto;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Soju;

public class DumbTransaction : IEquatable<DumbTransaction>
{
    public uint256 Id;
    public bool IsWasabi2Cj;
    public Dictionary<WalletId, HashSet<DumbCoin>> Inputs;
    public Dictionary<WalletId, HashSet<DumbCoin>> Outputs;

    public DumbTransaction(Dictionary<WalletId, HashSet<DumbCoin>>? inputs, Dictionary<WalletId, HashSet<DumbCoin>>? outputs) 
    {
        Id = RandomUtils.GetUInt256();

        IsWasabi2Cj = false;

        if (inputs is not null) Inputs = new Dictionary<WalletId, HashSet<DumbCoin>>(inputs);
        else Inputs = new Dictionary<WalletId, HashSet<DumbCoin>>();

        if (outputs is not null) Outputs = new Dictionary<WalletId, HashSet<DumbCoin>>(outputs);
        else Outputs = new Dictionary<WalletId, HashSet<DumbCoin>>();
    }

    public bool TryAddInput(WalletId walletId, DumbCoin input)
    {
        if (!Inputs.TryGetValue(walletId, out var coins)) 
        {
            coins = [];
            Inputs[walletId] = coins;
        }
        return coins.Add(input);
    }

    public bool TryAddOutput(WalletId walletId, DumbCoin output)
    {
        if (!Outputs.TryGetValue(walletId, out var coins)) 
        {
            coins = [];
            Outputs[walletId] = coins;
        }
        return coins.Add(output);
    }

    public uint256 GetHash()
    {
        return Id;
    }

    public override int GetHashCode()
    {
    unchecked
    {
        long hash = 17;

        foreach (var input in Inputs.ToList())
        {
            hash += input.GetHashCode()*31;
        }
        foreach (var output in Outputs.ToList())        
        {
            hash += output.GetHashCode()*31;
        }

        return (int)hash;
    }}

    public override bool Equals(object? obj) => Equals(obj as DumbCoin);

    public bool Equals(DumbTransaction? other) => this == other;

    public static bool operator ==(DumbTransaction? x, DumbTransaction? y) 
    {
        if (x is null && y is null) return true;
        if ((x is null && y is not null) || (x is not null && y is null)) return false;

        if (x is null || y is null) throw new ArgumentNullException();

        if (x.GetHashCode() != y.GetHashCode()) return false;

        return true; // hack before implementing the whole class
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
        if (Inputs != null) 
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
        if (Outputs != null)
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
