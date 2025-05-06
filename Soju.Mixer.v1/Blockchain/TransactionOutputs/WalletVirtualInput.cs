using NBitcoin;

namespace Soju.Blockchain.TransactionOutputs;

public class WalletVirtualInput
{
	public WalletVirtualInput(byte[] id, ISet<DumbCoin> coins)
	{
		Id = id;
		Coins = coins;
		KeyId = coins.Select(x => x.KeyId).Distinct().Single();
		AnonymitySet = coins.Where(x => x.KeyId == KeyId).Select(x => x.AnonymitySet).Single();
		Amount = coins.Sum(x => x.Amount);
	}

	public byte[] Id { get; }
	public ISet<DumbCoin> Coins { get; }
	public byte[] KeyId { get; }
	public double AnonymitySet { get; }
	public Money Amount { get; }
}
