using NBitcoin;
using System.Collections.Generic;
using System.Linq;

namespace Soju;

public class WalletVirtualOutput
{
	public WalletVirtualOutput(byte[] id, ISet<DumbCoin> coins)
	{
		Id = id;
		Coins = coins;
		Amount = coins.Sum(x => x.Amount);
	}

	public byte[] Id { get; }
    public ISet<DumbCoin> Coins { get; }
	public Money Amount { get; }
}
