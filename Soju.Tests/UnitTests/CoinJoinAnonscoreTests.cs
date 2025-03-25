using NBitcoin;
using Soju.Analysis;
using Soju.Helpers;
using Soju.Wallets;

namespace Soju.Tests.UnitTests;

public class CoinJoinAnonScoreTests
{
    [Fact]
    public void BasicCalculation()
    {
        var analyzer = new BlockchainAnalyzer();
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(9, Enumerable.Repeat(Money.Coins(1m), 9).ToArray(),
            [(Money.Coins(1.1m), 1)], [(Money.Coins(1m), Int32.MaxValue)], ownWalletId, false);

        analyzer.Analyze(tx);

        Assert.Equal(1, tx.Inputs[ownWalletId].First().AnonymitySet);

        // 10 participants, 1 is you, your anonset is 10.
        Assert.Equal(10, tx.Outputs[ownWalletId].First().AnonymitySet);
    }
}