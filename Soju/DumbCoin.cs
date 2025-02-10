using NBitcoin;
using System.Text;

namespace Soju;

public class DumbCoin : ISmartCoin, IEquatable<DumbCoin>
{
	public DumbTransaction Transaction;
    public Money Amount { get; }
    public ScriptType ScriptType { get; }
	public double AnonymitySet { get; set; }
	public uint256 TransactionId { get; }
	public OutPoint OutPoint { get; }
	public byte[] KeyId { get; }
	public uint Index { get; }
	public bool IsSufficientlyDistancedFromExternalKeys { get; }

    public DumbCoin(DumbTransaction? transaction, Money amount, ScriptType scriptType, double anonymitySet, uint index)
    {
		Transaction = transaction ?? new DumbTransaction(null, null);
        Amount = amount;
        ScriptType = scriptType;
        AnonymitySet = anonymitySet;
        TransactionId = Transaction.Id;
		KeyId = RandomUtils.GetBytes(32);
		Index = index;
        IsSufficientlyDistancedFromExternalKeys = true;

		OutPoint = new OutPoint(TransactionId, Index);
    }

	public override string ToString() 
	{
		StringBuilder sb = new();

		sb.Append($"amount: {Amount}\n");
		sb.Append($"script type: {ScriptType}\n");
		sb.Append($"anonset: {AnonymitySet}\n");
		sb.Append($"txid: {TransactionId}\n");
		sb.Append($"index: {Index}\n");
		sb.Append($"outpoint: {OutPoint}\n");
		sb.Append($"key: {KeyId}\n");

		return sb.ToString();
	}

    #region EqualityAndComparison

	public override bool Equals(object? obj) => Equals(obj as DumbCoin);

	public bool Equals(DumbCoin? other) => this == other;

	public override int GetHashCode() 
    {
        return 17 + 31*TransactionId.GetHashCode() + 31*31*Index.GetHashCode();
    }

	public static bool operator ==(DumbCoin? x, DumbCoin? y)
	{
		if (ReferenceEquals(x, y))
		{
			return true;
		}
		else if (x is null || y is null)
		{
			return false;
		}

		// Indices are fast to compare, so compare them first.
		return (y.Index == x.Index) && (y.TransactionId == x.TransactionId);
	}

	public static bool operator !=(DumbCoin? x, DumbCoin? y) => !(x == y);

	#endregion EqualityAndComparison
}
