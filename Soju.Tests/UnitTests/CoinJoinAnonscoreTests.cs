using NBitcoin;
using Soju.Analysis;
using Soju.Helpers;
using Soju.Wallets;

namespace Soju.Tests.UnitTests;

public class CoinJoinAnonScoreTests
{
    private static int DefaultHighAnonymitySet = Int32.MaxValue;
    
    [Fact]
    public void BasicCalculation()
    {
        var analyzer = new BlockchainAnalyzer();
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(9, Enumerable.Repeat(Money.Coins(1m), 9).ToArray(),
            [(Money.Coins(1.1m), 1)], [(Money.Coins(1m), DefaultHighAnonymitySet)], ownWalletId, false);

        analyzer.Analyze(tx);

        Assert.Equal(1, tx.Inputs[ownWalletId].First().AnonymitySet);

        // 10 participants, 1 is you, your anonset is 10.
        Assert.Equal(10, tx.Outputs[ownWalletId].First().AnonymitySet);
    }
    
    [Fact]
    public void DoubleProcessing()
    {
        var analyzer = new BlockchainAnalyzer();
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(9, Enumerable.Repeat(Money.Coins(1m), 9).ToArray(), 
            [(Money.Coins(1.1m), 1)], [(Money.Coins(1m), DefaultHighAnonymitySet)], ownWalletId, false);
        analyzer.Analyze(tx);
        analyzer.Analyze(tx);
        Assert.Equal(1, tx.Inputs[ownWalletId].First().AnonymitySet);

        // 10 participants, 1 is you, your anonset is 10.
        Assert.Equal(10, tx.Outputs[ownWalletId].First().AnonymitySet);
    }
    
    [Fact]
    public void OtherWalletChangesThings()
    {
        var analyzer = new BlockchainAnalyzer();
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(9, Enumerable.Repeat(Money.Coins(1m), 8).ToArray(), new[] { (Money.Coins(1.1m), 1) }, new[] { (Money.Coins(1m), DefaultHighAnonymitySet), (Money.Coins(1m), DefaultHighAnonymitySet) }, ownWalletId, false);
        var sc = tx.Outputs[ownWalletId].First();
        sc.WalletId = new WalletId(Guid.NewGuid());
        analyzer.Analyze(tx);
        sc.WalletId = ownWalletId;
        analyzer.Analyze(tx);
        Assert.Equal(1, tx.Inputs[ownWalletId].First().AnonymitySet);

        // 10 participants, 2 is you, your anonset is 10/2 = 5.
        Assert.Equal(5, tx.Outputs[ownWalletId].First().AnonymitySet);
        Assert.Equal(5, tx.Outputs[ownWalletId].Skip(1).First().AnonymitySet);
    }
    
    [Fact]
    public void Inheritance()
    {
        var analyzer = new BlockchainAnalyzer();
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(9, Enumerable.Repeat(Money.Coins(1m), 9).ToArray(), new[] { (Money.Coins(1.1m), 100) }, new[] { (Money.Coins(1m), DefaultHighAnonymitySet) }, ownWalletId, false);

        analyzer.Analyze(tx);

        Assert.Equal(100, tx.Inputs[ownWalletId].First().AnonymitySet);

        // 10 participants, 1 is you, your anonset is 10 and you inherit 99 anonset,
        // because you don't want to count yourself twice.
        Assert.Equal(109, tx.Outputs[ownWalletId].First().AnonymitySet);
    }
    
    [Fact]
    public void ChangeOutput()
    {
        var analyzer = new BlockchainAnalyzer();
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(9, Enumerable.Repeat(Money.Coins(1m), 9).ToArray(), new[] { (Money.Coins(6.2m), 1) }, new[] { (Money.Coins(1m), DefaultHighAnonymitySet), (Money.Coins(5m), DefaultHighAnonymitySet) }, ownWalletId, false);

        analyzer.Analyze(tx);

        var active = tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(1m));
        var change = tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(5m));

        Assert.Equal(1, tx.Inputs[ownWalletId].First().AnonymitySet);
        Assert.Equal(10, active.AnonymitySet);
        Assert.Equal(1, change.AnonymitySet);
    }
    
    [Fact]
    public void ChangeOutputConservativeConsolidation()
    {
        var analyzer = new BlockchainAnalyzer();
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(9, Enumerable.Repeat(Money.Coins(1m), 9).ToArray(), new[] { (Money.Coins(3.1m), 1), (Money.Coins(3.1m), 100) }, new[] { (Money.Coins(1m), DefaultHighAnonymitySet), (Money.Coins(5m), DefaultHighAnonymitySet) }, ownWalletId, false);

        analyzer.Analyze(tx);

        var active = tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(1m));
        var change = tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(5m));

        Assert.Equal(1, tx.Inputs[ownWalletId].First().AnonymitySet);
        Assert.Equal(59.5, active.AnonymitySet);
        Assert.Equal(1, change.AnonymitySet);
    }
    
    [Fact]
    public void ChangeOutputInheritance()
    {
        var analyzer = new BlockchainAnalyzer();
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(9, Enumerable.Repeat(Money.Coins(1m), 9).ToArray(), new[] { (Money.Coins(6.2m), 100) }, new[] { (Money.Coins(1m), DefaultHighAnonymitySet), (Money.Coins(5m), DefaultHighAnonymitySet) }, ownWalletId, false);

        analyzer.Analyze(tx);

        var active = tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(1m));
        var change = tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(5m));

        Assert.Equal(100, tx.Inputs[ownWalletId].First().AnonymitySet);
        Assert.Equal(109, active.AnonymitySet);
        Assert.Equal(100, change.AnonymitySet);
    }
    
    [Fact]
    public void MultiDenomination()
    {
        // Multiple standard denomination outputs should be accounted separately.
        var analyzer = new BlockchainAnalyzer();
        var othersOutputs = new[] { 1, 1, 1, 2, 2 };
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(
            9,
            othersOutputs.Select(x => Money.Coins(x)).ToArray(),
            new[] { (Money.Coins(3.2m), 1) },
            new[] { (Money.Coins(1m), DefaultHighAnonymitySet), (Money.Coins(2m), DefaultHighAnonymitySet) },
            ownWalletId,
            false);

        analyzer.Analyze(tx);

        var level1 = tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(1m));
        var level2 = tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(2m));

        Assert.Equal(1, tx.Inputs[ownWalletId].First().AnonymitySet);
        Assert.Equal(4, level1.AnonymitySet);
        Assert.Equal(3, level2.AnonymitySet);
    }
    
    [Fact]
    public void MultiDenominationInheritance()
    {
        // Multiple denominations inherit properly.
        var analyzer = new BlockchainAnalyzer();
        var othersOutputs = new[] { 1, 1, 1, 2, 2 };
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(
            9,
            othersOutputs.Select(x => Money.Coins(x)).ToArray(),
            new[] { (Money.Coins(3.2m), 100) },
            new[] { (Money.Coins(1m), DefaultHighAnonymitySet), (Money.Coins(2m), DefaultHighAnonymitySet) },
            ownWalletId,
            false);

        analyzer.Analyze(tx);

        var level1 = tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(1m));
        var level2 = tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(2m));

        Assert.Equal(100, tx.Inputs[ownWalletId].First().AnonymitySet);
        Assert.Equal(103, level1.AnonymitySet);
        Assert.Equal(102, level2.AnonymitySet);
    }
    
    [Fact]
    public void SelfAnonsetSanityCheck()
    {
        // If we have multiple same denomination in the same coinjoin, then our anonset would be total coins/our coins.
        var analyzer = new BlockchainAnalyzer();
        var othersOutputs = new[] { 1, 1, 1 };
        var ownOutputs = new[] { 1, 1 };
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(
            9,
            othersOutputs.Select(x => Money.Coins(x)).ToArray(),
            new[] { (Money.Coins(3.2m), 1) },
            ownOutputs.Select(x => (Money.Coins(x), DefaultHighAnonymitySet)).ToArray(),
            ownWalletId,
            false);

        analyzer.Analyze(tx);

        Assert.Equal(1, tx.Inputs[ownWalletId].First().AnonymitySet);
        Assert.All(tx.Outputs[ownWalletId], x => Assert.Equal(5 / 2d, x.AnonymitySet));
    }

    [Fact]
    public void SelfAnonsetSanityCheck2()
    {
        var analyzer = new BlockchainAnalyzer();
        var othersOutputs = new[] { 1 };
        var ownOutputs = new[] { 1, 1, 1, 1 };
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(
            1,
            othersOutputs.Select(x => Money.Coins(x)).ToArray(),
            new[] { (Money.Coins(4.2m), 4) },
            ownOutputs.Select(x => (Money.Coins(x), DefaultHighAnonymitySet)).ToArray(),
            ownWalletId,
            false);

        Assert.Equal(4, tx.Inputs[ownWalletId].First().AnonymitySet);
        analyzer.Analyze(tx);
        Assert.Equal(4, tx.Inputs[ownWalletId].First().AnonymitySet);

        // The increase in the anonymity set would naively be 1 as there is 1 equal non-wallet output.
        // Since 4 outputs are ours, we divide the increase in anonymity between them
        // and add that to the inherited anonymity of 4.
        Assert.All(tx.Outputs[ownWalletId], x => Assert.Equal(4 + (1 / 4d), x.AnonymitySet));
    }
    
    [Fact]
    public void InputSanityCheck()
    {
        // Anonset can never be larger than the number of inputs.
        var analyzer = new BlockchainAnalyzer();
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(2, Enumerable.Repeat(Money.Coins(1m), 9).ToArray(), new[] { (Money.Coins(1.1m), 1) }, new[] { (Money.Coins(1m), DefaultHighAnonymitySet) }, ownWalletId, false);

        analyzer.Analyze(tx);

        Assert.Equal(1, tx.Inputs[ownWalletId].First().AnonymitySet);
        Assert.Equal(3, tx.Outputs[ownWalletId].First().AnonymitySet);
    }
    
    [Fact]
    public void SelfAnonsetSanityBeforeInputSanityCheck()
    {
        // Self anonset sanity check is executed before input sanity check is executed.
        var analyzer = new BlockchainAnalyzer();
        var othersOutputs = new[] { 1, 1, 1 };
        var ownOutputs = new[] { 1, 1 };
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(
            1,
            othersOutputs.Select(x => Money.Coins(x)).ToArray(),
            new[] { (Money.Coins(3.2m), 1) },
            ownOutputs.Select(x => (Money.Coins(x), DefaultHighAnonymitySet)).ToArray(),
            ownWalletId,
            false);

        analyzer.Analyze(tx);

        Assert.Equal(1, tx.Inputs[ownWalletId].First().AnonymitySet);

        // The increase in the anonymity set would naively be 3 as there are 3 equal non-wallet outputs.
        // But there is only 1 non-wallet input, so that limits the increase to 1.
        // We are getting an anonymity set of 1 + min(3/2, 1) = 1 + 1 = 2.
        Assert.All(tx.Outputs[ownWalletId], x => Assert.Equal(2, x.AnonymitySet));
    }
    
    [Fact]
    public void InputMergePunishmentNoInheritance()
    {
        // Input merging results in worse inherited anonset, but does not punish gains from output indistinguishability.
        var analyzer = new BlockchainAnalyzer();
        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        var tx = BitcoinFactory.CreateDumbTransaction(
            9,
            Enumerable.Repeat(Money.Coins(1m), 9).ToArray(),
            new[] { (Money.Coins(1.1m), 1), (Money.Coins(1.2m), 1), (Money.Coins(1.3m), 1), (Money.Coins(1.4m), 1) },
            new[] { (Money.Coins(1m), DefaultHighAnonymitySet) },
            ownWalletId,
            false);

        analyzer.Analyze(tx);

        Assert.All(tx.Inputs[ownWalletId], x => Assert.Equal(1, x.AnonymitySet));

        // 10 participants, 1 is you, your anonset would be 10 normally and now too:
        Assert.Equal(10, tx.Outputs[ownWalletId].First().AnonymitySet);
    }
    
    [Fact]
    public void InputMergeNonStandardChange()
    {
        // Input merging and non-standard change results in maximum anonymity punishment.
        var analyzer = new BlockchainAnalyzer();

        var ownInputs = new[] { (Money.Coins(1.1m), 100), (Money.Coins(1.2m), 1) };
        var othersOutputCount = 9;

        WalletId ownWalletId = new WalletId(Guid.NewGuid());

        var tx = BitcoinFactory.CreateDumbTransaction(
            50,
            Enumerable.Repeat(Money.Coins(1m), othersOutputCount).ToArray(),
            ownInputs,
            new[] { (Money.Coins(1m), DefaultHighAnonymitySet), (Money.Satoshis(5001), DefaultHighAnonymitySet) },
            ownWalletId,
            false);

        analyzer.Analyze(tx);

        var active = tx.Outputs[ownWalletId].MaxBy(x => x.Amount)!;
        var change = tx.Outputs[ownWalletId].MinBy(x => x.Amount)!;

        var weightedAverage = ownInputs.Sum(x => x.Item1.Satoshi * x.Item2) / ownInputs.Sum(x => x.Item1.Satoshi);
        var maxPunishment = ownInputs.Min(x => x.Item2);

        Assert.Equal(weightedAverage + othersOutputCount, active.AnonymitySet, precision: 0);
        Assert.Equal(maxPunishment, change.AnonymitySet, precision: 0);
    }
    
    [Fact]
    public void InputMergeSmallUniqueDenom()
    {
        // Input merging and small unique denomination in WW2 results in no anonymity punishment.
        var analyzer = new BlockchainAnalyzer();

        var ownInputs = new[] { (Money.Coins(1.1m), 100), (Money.Coins(1.2m), 1) };
        var othersOutputCount = 9;

        WalletId ownWalletId = new WalletId(Guid.NewGuid());

        var tx = BitcoinFactory.CreateDumbTransaction(
            50,
            Enumerable.Repeat(Money.Coins(1m), othersOutputCount).ToArray(),
            ownInputs,
            new[] { (Money.Coins(1m), DefaultHighAnonymitySet), (Money.Satoshis(5000), DefaultHighAnonymitySet) },
            ownWalletId,
            orderByAmount: true);

        analyzer.Analyze(tx);

        var active = tx.Outputs[ownWalletId].MaxBy(x => x.Amount)!;
        var change = tx.Outputs[ownWalletId].MinBy(x => x.Amount)!;

        var weightedAverage = ownInputs.Sum(x => x.Item1.Satoshi * x.Item2) / ownInputs.Sum(x => x.Item1.Satoshi);
        var maxPunishment = ownInputs.Min(x => x.Item2);

        Assert.True(tx.IsWasabi2Cj);
        Assert.Equal(weightedAverage + othersOutputCount, active.AnonymitySet, precision: 0);
        Assert.NotEqual(maxPunishment, change.AnonymitySet, precision: 0);
        Assert.Equal(weightedAverage, change.AnonymitySet, precision: 0);
    }
    
    [Fact]
    public void InputMergeLargeUniqueDenom()
    {
        // Input merging and large unique denomination in WW2 results in maximum anonymity punishment in relation to the largest inputs: https://github.com/WalletWasabi/WalletWasabi/pull/10699/
        var analyzer = new BlockchainAnalyzer();

        var ownInputs = new[] { (Money.Coins(1.1m), 100), (Money.Coins(2.2m), 1) };
        var othersOutputCount = 9;

        WalletId ownWalletId = new WalletId(Guid.NewGuid());
        
        var tx = BitcoinFactory.CreateDumbTransaction(
            50,
            Enumerable.Repeat(Money.Coins(1m), othersOutputCount).ToArray(),
            ownInputs,
            new[] { (Money.Coins(1m), DefaultHighAnonymitySet), (Money.Coins(2m), DefaultHighAnonymitySet) },
            ownWalletId,
            orderByAmount: true);

        analyzer.Analyze(tx);

        var active = tx.Outputs[ownWalletId].MinBy(x => x.Amount)!;
        var change = tx.Outputs[ownWalletId].MaxBy(x => x.Amount)!;

        var weightedAverage = ownInputs.Sum(x => x.Item1.Satoshi * x.Item2) / ownInputs.Sum(x => x.Item1.Satoshi);
        var maxPunishment = ownInputs.Min(x => x.Item2);

        Assert.True(tx.IsWasabi2Cj);
        Assert.Equal(weightedAverage + othersOutputCount, active.AnonymitySet, precision: 0);
        Assert.NotEqual(weightedAverage, change.AnonymitySet, precision: 0);
        Assert.Equal(maxPunishment, change.AnonymitySet, precision: 0);
    }
    
    [Fact]
    public void InputMergeLargeUniqueDenomReasonablePunishment()
    {
        // Input merging and large unique denomination in WW2 results in maximum anonymity punishment in relation to the largest inputs: https://github.com/WalletWasabi/WalletWasabi/pull/10699/
        var analyzer = new BlockchainAnalyzer();

        var ownInputs = new[] { (Money.Coins(1.1m), 1), (Money.Coins(55m), 100), (Money.Coins(45m), 3) };
        var othersOutputCount = 9;

        WalletId ownWalletId = new WalletId(Guid.Parse("90902eeb-0977-4283-84d4-9a408b0f8829"));
        
        var tx = BitcoinFactory.CreateDumbTransaction(
            50,
            Enumerable.Repeat(Money.Coins(1m), othersOutputCount).ToArray(),
            ownInputs,
            new[] { (Money.Coins(1m), DefaultHighAnonymitySet), (Money.Coins(100m), DefaultHighAnonymitySet) },
            ownWalletId,
            orderByAmount: true);

        analyzer.Analyze(tx);

        var active = tx.Outputs[ownWalletId].MinBy(x => x.Amount)!;
        var change = tx.Outputs[ownWalletId].MaxBy(x => x.Amount)!;

        var weightedAverage = ownInputs.Sum(x => x.Item1.Satoshi * x.Item2) / ownInputs.Sum(x => x.Item1.Satoshi);

        Assert.True(tx.IsWasabi2Cj);
        Assert.Equal(weightedAverage + othersOutputCount, active.AnonymitySet, precision: 0, MidpointRounding.ToZero);
        Assert.NotEqual(weightedAverage, change.AnonymitySet, precision: 0);
        Assert.Equal(3, change.AnonymitySet, precision: 0);
    }
    
    [Fact]
    public void InputMergeLargeUniqueDenomsReasonablePunishment()
    {
        // Input merging and large unique denominations in WW2 results in maximum anonymity punishment in relation to the largest inputs: https://github.com/WalletWasabi/WalletWasabi/pull/10699/
        var analyzer = new BlockchainAnalyzer();

        var ownInputs = new[] { (Money.Coins(1.1m), 1), (Money.Coins(55m), 100), (Money.Coins(45m), 3) };
        var othersOutputCount = 9;

        WalletId ownWalletId = new WalletId(Guid.NewGuid());

        var tx = BitcoinFactory.CreateDumbTransaction(
            50,
            Enumerable.Repeat(Money.Coins(1m), othersOutputCount).ToArray(),
            ownInputs,
            new[]
            {
                (Money.Satoshis(5000m), DefaultHighAnonymitySet),
                (Money.Coins(1m), DefaultHighAnonymitySet),
                (Money.Coins(20m), DefaultHighAnonymitySet),
                (Money.Coins(20m), DefaultHighAnonymitySet),
                (Money.Coins(50m), DefaultHighAnonymitySet)
            },
            ownWalletId,
            orderByAmount: true);

        analyzer.Analyze(tx);

        var weightedAverage = ownInputs.Sum(x => x.Item1.Satoshi * x.Item2) / ownInputs.Sum(x => x.Item1.Satoshi);

        Assert.True(tx.IsWasabi2Cj);

        Assert.Equal(weightedAverage, tx.Outputs[ownWalletId].First(x => x.Amount == Money.Satoshis(5000m)).AnonymitySet, precision: 0, MidpointRounding.ToZero);
        Assert.Equal(weightedAverage + othersOutputCount, tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(1m)).AnonymitySet, precision: 0, MidpointRounding.ToZero);
        Assert.Equal(3, tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(20m)).AnonymitySet, precision: 0);
        Assert.Equal(3, tx.Outputs[ownWalletId].Where(x => x.Amount == Money.Coins(20m)).Skip(1).First().AnonymitySet, precision: 0);
        Assert.Equal(3, tx.Outputs[ownWalletId].First(x => x.Amount == Money.Coins(50m)).AnonymitySet, precision: 0);
    }
}