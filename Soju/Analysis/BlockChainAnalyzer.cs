using System.Collections.Immutable;

namespace Soju.Analysis;

public class BlockchainAnalyzer
{
	public static readonly long[] StdDenoms = new[]
	{
		5000L, 6561L, 8192L, 10000L, 13122L, 16384L, 19683L, 20000L, 32768L, 39366L, 50000L, 59049L, 65536L, 100000L, 118098L,
		131072L, 177147L, 200000L, 262144L, 354294L, 500000L, 524288L, 531441L, 1000000L, 1048576L, 1062882L, 1594323L, 2000000L,
		2097152L, 3188646L, 4194304L, 4782969L, 5000000L, 8388608L, 9565938L, 10000000L, 14348907L, 16777216L, 20000000L,
		28697814L, 33554432L, 43046721L, 50000000L, 67108864L, 86093442L, 100000000L, 129140163L, 134217728L, 200000000L,
		258280326L, 268435456L, 387420489L, 500000000L, 536870912L, 774840978L, 1000000000L, 1073741824L, 1162261467L,
		2000000000L, 2147483648L, 2324522934L, 3486784401L, 4294967296L, 5000000000L, 6973568802L, 8589934592L, 10000000000L,
		10460353203L, 17179869184L, 20000000000L, 20920706406L, 31381059609L, 34359738368L, 50000000000L, 62762119218L,
		68719476736L, 94143178827L, 100000000000L, 137438953472L
	};

    public void Analyze(DumbTransaction tx)
    {
        foreach (var walletId in tx.Inputs.Keys)
        {
            AnalyzeCoinjoinWalletInputs(tx, walletId, out StartingAnonScores startingAnonScores);
            AnalyzeCoinjoinWalletOutputs(tx, walletId, startingAnonScores);

            double startingOutputAnonset = startingAnonScores.WeightedAverage.standard;

            AdjustWalletInputs(tx, startingOutputAnonset);
        }
    }

    private static void AnalyzeCoinjoinWalletInputs(
		DumbTransaction tx,
        WalletId walletId,
		out StartingAnonScores startingAnonScores)
	{
		CoinjoinAnalyzer cjAnal = new(tx);

		// Consolidation in coinjoins is the only type of consolidation that's acceptable,
		// because coinjoins are an exception from common input ownership heuristic.
		// However this is not always true:
		// For cases when it is we calculate weighted average.
		// For cases when it isn't we calculate the rest.
		CalculateWeightedAverage(tx, walletId, cjAnal, out double mixedAnonScore, out double mixedAnonScoreSanctioned);
		CalculateMinAnonScore(tx, walletId, cjAnal, out double nonMixedAnonScore, out double nonMixedAnonScoreSanctioned);
		CalculateHalfMixedAnonScore(tx, walletId, cjAnal, mixedAnonScore, mixedAnonScoreSanctioned, out double halfMixedAnonScore, out double halfMixedAnonScoreSanctioned);

		startingAnonScores = new()
		{
			Minimum = (nonMixedAnonScore, nonMixedAnonScoreSanctioned),
			BigInputMinimum = (halfMixedAnonScore, halfMixedAnonScoreSanctioned),
			WeightedAverage = (mixedAnonScore, mixedAnonScoreSanctioned)
		};
	}

    	private static void CalculateHalfMixedAnonScore(DumbTransaction tx, WalletId walletId, CoinjoinAnalyzer cjAnal, double mixedAnonScore, double mixedAnonScoreSanctioned, out double halfMixedAnonScore, out double halfMixedAnonScoreSanctioned)
	{
		// Calculate punishment to the smallest anonscore input from the largest inputs.
		// We know WW2 coinjoins order inputs by amount.
		var ourLargeKeyIds = new HashSet<byte[]>();

        // This whole thing is really bad
        List<(WalletId walletId, DumbCoin coin)> sortedInputs = [];
        foreach (var pair in tx.Inputs)
        {
            foreach(var entry in pair.Value)
            {
                sortedInputs.Add((pair.Key, entry));
            }
        }
        sortedInputs.Sort((x, y) => x.coin.Amount.CompareTo(y.coin.Amount));

        for (int i = 0; i < sortedInputs.Count; i++)
        {
            if (sortedInputs[i].walletId == walletId) ourLargeKeyIds.Add(sortedInputs[i].coin.KeyId);
        }

        IReadOnlyCollection<WalletVirtualInput> walletVirtualInputs = tx.Inputs[walletId]
            .Select(x => new WalletVirtualInput(x.KeyId, (HashSet<DumbCoin>)[x]))
            .ToImmutableArray();

		halfMixedAnonScore = CoinjoinAnalyzer.Min(walletVirtualInputs.Where(x => ourLargeKeyIds.Contains(x.KeyId)).Select(x => new CoinjoinAnalyzer.AmountWithAnonymity(x.AnonymitySet, x.Amount)));
		halfMixedAnonScoreSanctioned = CoinjoinAnalyzer.Min(walletVirtualInputs.Where(x => ourLargeKeyIds.Contains(x.KeyId)).Select(x => new CoinjoinAnalyzer.AmountWithAnonymity(x.AnonymitySet + cjAnal.ComputeInputSanction(x, walletId, CoinjoinAnalyzer.Min), x.Amount)));

		// Sanity check: make sure to not give more than the weighted average would.
		halfMixedAnonScore = Math.Min(halfMixedAnonScore, mixedAnonScore);
		halfMixedAnonScoreSanctioned = Math.Min(halfMixedAnonScoreSanctioned, mixedAnonScoreSanctioned);
	}

	private static void CalculateMinAnonScore(DumbTransaction tx, WalletId walletId, CoinjoinAnalyzer cjAnal, out double nonMixedAnonScore, out double nonMixedAnonScoreSanctioned)
	{
		// Calculate punishment to the smallest anonscore input.
        IReadOnlyCollection<WalletVirtualInput> walletVirtualInputs = tx.Inputs[walletId]
            .Select(x => new WalletVirtualInput(x.KeyId, (HashSet<DumbCoin>)[x]))
            .ToImmutableArray();
		nonMixedAnonScore = CoinjoinAnalyzer.Min(walletVirtualInputs.Select(x => new CoinjoinAnalyzer.AmountWithAnonymity(x.AnonymitySet, x.Amount)));
		nonMixedAnonScoreSanctioned = CoinjoinAnalyzer.Min(walletVirtualInputs.Select(x => new CoinjoinAnalyzer.AmountWithAnonymity(x.AnonymitySet + cjAnal.ComputeInputSanction(x, walletId, CoinjoinAnalyzer.Min), x.Amount)));
	}

	private static void CalculateWeightedAverage(DumbTransaction tx, WalletId walletId, CoinjoinAnalyzer cjAnal, out double mixedAnonScore, out double mixedAnonScoreSanctioned)
	{
		// Calculate weighted average.
        IReadOnlyCollection<WalletVirtualInput> walletVirtualInputs = tx.Inputs[walletId]
            .Select(x => new WalletVirtualInput(x.KeyId, (HashSet<DumbCoin>)[x]))
            .ToImmutableArray();
		mixedAnonScore = CoinjoinAnalyzer.WeightedAverage(walletVirtualInputs.Select(x => new CoinjoinAnalyzer.AmountWithAnonymity(x.AnonymitySet, x.Amount)));
		mixedAnonScoreSanctioned = CoinjoinAnalyzer.WeightedAverage(walletVirtualInputs.Select(x => new CoinjoinAnalyzer.AmountWithAnonymity(x.AnonymitySet + cjAnal.ComputeInputSanction(x, walletId, CoinjoinAnalyzer.WeightedAverage), x.Amount)));
	}

    private void AnalyzeCoinjoinWalletOutputs(
		DumbTransaction tx,
        WalletId walletId,
		StartingAnonScores startingAnonScores)
	{
        IReadOnlyCollection<DumbCoin> foreignInputs = tx.Inputs.Where(x => x.Key != walletId).SelectMany(x => x.Value).ToHashSet();
        
		var foreignInputCount = foreignInputs.Count;
		long? maxAmountWeightedAverageIsApplicableFor = null;

		foreach (var virtualOutput in tx.WalletVirtualOutputs)
		{
			(double standard, double sanctioned) startingOutputAnonset;

			// If the virtual output has a nonempty anonymity set
			if (!tx.ForeignVirtualOutputs.Any(x => x.Amount == virtualOutput.Amount))
			{
				// When WW2 denom output isn't too large, then it's not change.
				if (tx.IsWasabi2Cj is true && StdDenoms.Contains(virtualOutput.Amount.Satoshi))
				{
					if (maxAmountWeightedAverageIsApplicableFor is null && !TryGetLargestEqualForeignOutputAmount(tx, out maxAmountWeightedAverageIsApplicableFor))
					{
						maxAmountWeightedAverageIsApplicableFor = Constants.MaximumNumberOfSatoshis;
					}

					startingOutputAnonset = virtualOutput.Amount <= maxAmountWeightedAverageIsApplicableFor
						? startingAnonScores.WeightedAverage
						: startingAnonScores.BigInputMinimum;
				}
				else
				{
					startingOutputAnonset = startingAnonScores.Minimum;
				}
			}
			else
			{
				startingOutputAnonset = startingAnonScores.WeightedAverage;
			}

			// Anonset gain cannot be larger than others' input count.
			// Picking randomly an output would make our anonset: total/ours.
			double anonymityGain = Math.Min(CoinjoinAnalyzer.ComputeAnonymityContribution(virtualOutput.Coins.First()), foreignInputCount);

			// Account for the inherited anonymity set size from the inputs in the
			// anonymity set size estimate.
			double anonset = new[] { startingOutputAnonset.sanctioned + anonymityGain, anonymityGain + 1, startingOutputAnonset.standard }.Max();

			foreach (var hdPubKey in virtualOutput.Coins.Select(x => x.HdPubKey).ToHashSet())
			{
				uint256 txid = tx.GetHash();
				if (hdPubKey.AnonymitySet == HdPubKey.DefaultHighAnonymitySet)
				{
					// If the new coin's HD pubkey haven't been used yet
					// then its anonset haven't been set yet.
					// In that case the acquired anonset does not have to be intersected with the default anonset,
					// so this coin gets the acquired anonset.
					hdPubKey.SetAnonymitySet(anonset, txid);
				}
				else if (tx.WalletVirtualInputs.Select(x => x.HdPubKey).Contains(hdPubKey))
				{
					// If it's a reuse of an input's pubkey, then intersection punishment is senseless.
					hdPubKey.SetAnonymitySet(startingOutputAnonset.sanctioned, txid);
				}
				else if (hdPubKey.HistoricalAnonSet.ContainsKey(txid))
				{
					// If we already processed this transaction for this script
					// then we'll go with normal processing.
					// It may be a duplicated processing or new information arrived (like other wallet loaded)
					// If there are more anonsets already
					// then it's address reuse that we have already punished so leave it alone.
					if (hdPubKey.HistoricalAnonSet.Count == 1)
					{
						hdPubKey.SetAnonymitySet(anonset, txid);
					}
				}
				else
				{
					// It's address reuse.
					hdPubKey.SetAnonymitySet(Intersect(new[] { anonset, hdPubKey.AnonymitySet }), txid);
				}
			}
		}
	}
}