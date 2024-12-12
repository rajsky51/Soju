using NBitcoin;
using NBitcoin.Crypto;
using System.Diagnostics.CodeAnalysis;

namespace Soju;

public class DumbTransaction : IEquatable<DumbTransaction>
{
    public uint256 Id;
    public Dictionary<WalletId, HashSet<DumbCoin>> Inputs;
    public Dictionary<WalletId, HashSet<DumbCoin>> Outputs;

    public DumbTransaction(Dictionary<WalletId, HashSet<DumbCoin>>? inputs, Dictionary<WalletId, HashSet<DumbCoin>>? outputs) 
    {
        Id = RandomUtils.GetUInt256();

        if (inputs is not null) Inputs = new Dictionary<WalletId, HashSet<DumbCoin>>(inputs);
        else Inputs = new Dictionary<WalletId, HashSet<DumbCoin>>();

        if (outputs is not null) Outputs = new Dictionary<WalletId, HashSet<DumbCoin>>(outputs);
        else Outputs = new Dictionary<WalletId, HashSet<DumbCoin>>();
    }

    public bool TryAddInput(WalletId walletId, DumbCoin input) => Inputs[walletId].Add(input);

    public bool TryAddOutput(WalletId walletId, DumbCoin output) => Outputs[walletId].Add(output);

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
}