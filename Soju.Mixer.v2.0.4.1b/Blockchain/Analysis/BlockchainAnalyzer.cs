using System.Diagnostics.CodeAnalysis;
using NBitcoin;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.Transactions;
using Soju.Helpers;
using Soju.Wallets;

namespace Soju.Blockchain.Analysis;

public class BlockchainAnalyzer
{
	public static readonly long[] StdDenoms = StandardDenominationsProvider.StdDenoms;

    public void Analyze(DumbTransaction tx)
    {
        foreach (var walletId in tx.Outputs.Keys)
        {
            AnalyzeCoinjoinWalletInputs(tx, walletId, out StartingAnonScores startingAnonScores);
            AnalyzeCoinjoinWalletOutputs(tx, walletId, startingAnonScores);

            double startingOutputAnonset = startingAnonScores.WeightedAverage.standard;

            //AdjustWalletInputs(tx, walletId, startingOutputAnonset);
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
		List<WalletVirtualInput> walletVirtualInputs = [];
		if (tx.Inputs.TryGetValue(walletId, out var inputCoins))
		{
			foreach (DumbCoin coin in inputCoins)
			{
				walletVirtualInputs.Add(new WalletVirtualInput(coin.KeyId, (HashSet<DumbCoin>) [coin]));
			}
		}
		
		CalculateWeightedAverage(walletId, walletVirtualInputs, cjAnal, out double mixedAnonScore, out double mixedAnonScoreSanctioned);
		CalculateMinAnonScore(walletId, walletVirtualInputs, cjAnal, out double nonMixedAnonScore, out double nonMixedAnonScoreSanctioned);
		CalculateHalfMixedAnonScore(tx, walletId, walletVirtualInputs, cjAnal, mixedAnonScore, mixedAnonScoreSanctioned, out double halfMixedAnonScore, out double halfMixedAnonScoreSanctioned);

		startingAnonScores = new()
		{
			Minimum = (nonMixedAnonScore, nonMixedAnonScoreSanctioned),
			BigInputMinimum = (halfMixedAnonScore, halfMixedAnonScoreSanctioned),
			WeightedAverage = (mixedAnonScore, mixedAnonScoreSanctioned)
		};
	}

    	private static void CalculateHalfMixedAnonScore(DumbTransaction tx, WalletId walletId, IReadOnlyCollection<WalletVirtualInput> walletVirtualInputs, CoinjoinAnalyzer cjAnal, double mixedAnonScore, double mixedAnonScoreSanctioned, out double halfMixedAnonScore, out double halfMixedAnonScoreSanctioned)
	{
		// Calculate punishment to the smallest anonscore input from the largest inputs.
		// We know WW2 coinjoins order inputs by amount.
		var ourLargeKeyIds = new HashSet<byte[]>();

        // This whole thing is really bad
        List<(WalletId walletId, DumbCoin coin)> sortedInputs = [];
        foreach (var pair in tx.Inputs)
        {
            foreach(var coin in pair.Value)
            {
                sortedInputs.Add((pair.Key, coin));
            }
        }
        sortedInputs.Sort((x, y) => y.coin.Amount.CompareTo(x.coin.Amount));

        for (int i = 0; i < sortedInputs.Count; i++)
        {
	        if (sortedInputs[i].walletId == walletId) ourLargeKeyIds.Add(sortedInputs[i].coin.KeyId);
	        else break;
        }

        halfMixedAnonScore = CoinjoinAnalyzer.Min(walletVirtualInputs.Where(x => ourLargeKeyIds.Contains(x.KeyId)).Select(x => new CoinjoinAnalyzer.AmountWithAnonymity(x.AnonymitySet, x.Amount)));
		halfMixedAnonScoreSanctioned = CoinjoinAnalyzer.Min(walletVirtualInputs.Where(x => ourLargeKeyIds.Contains(x.KeyId)).Select(x => new CoinjoinAnalyzer.AmountWithAnonymity(x.AnonymitySet + cjAnal.ComputeInputSanction(x, walletId, CoinjoinAnalyzer.Min), x.Amount)));

		// Sanity check: make sure to not give more than the weighted average would.
		halfMixedAnonScore = Math.Min(halfMixedAnonScore, mixedAnonScore);
		halfMixedAnonScoreSanctioned = Math.Min(halfMixedAnonScoreSanctioned, mixedAnonScoreSanctioned);
	}

	private static void CalculateMinAnonScore(WalletId walletId, IReadOnlyCollection<WalletVirtualInput> walletVirtualInputs, CoinjoinAnalyzer cjAnal, out double nonMixedAnonScore, out double nonMixedAnonScoreSanctioned)
	{
		// Calculate punishment to the smallest anonscore input.
        // IReadOnlyCollection<WalletVirtualInput> walletVirtualInputs = tx.Inputs[walletId]
        //     .Select(x => new WalletVirtualInput(x.KeyId, (HashSet<DumbCoin>)[x]))
        //     .ToImmutableArray();
		nonMixedAnonScore = CoinjoinAnalyzer.Min(walletVirtualInputs.Select(x => new CoinjoinAnalyzer.AmountWithAnonymity(x.AnonymitySet, x.Amount)));
		nonMixedAnonScoreSanctioned = CoinjoinAnalyzer.Min(walletVirtualInputs.Select(x => new CoinjoinAnalyzer.AmountWithAnonymity(x.AnonymitySet + cjAnal.ComputeInputSanction(x, walletId, CoinjoinAnalyzer.Min), x.Amount)));
	}

	private static void CalculateWeightedAverage(WalletId walletId, IReadOnlyCollection<WalletVirtualInput> walletVirtualInputs, CoinjoinAnalyzer cjAnal, out double mixedAnonScore, out double mixedAnonScoreSanctioned)
	{
		// Calculate weighted average.
        // IReadOnlyCollection<WalletVirtualInput> walletVirtualInputs = tx.Inputs[walletId]
        //     .Select(x => new WalletVirtualInput(x.KeyId, (HashSet<DumbCoin>)[x]))
        //     .ToImmutableArray();
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

		var walletVirtualOutputs = tx.Outputs[walletId].Select(x => new WalletVirtualOutput(x.KeyId, (HashSet<DumbCoin>)[x]));
		var foreignVirtualOutputs = tx.Outputs
			.Where(x => x.Key != walletId)
			.SelectMany(x => x.Value)
			.Select(x => new ForeignVirtualOutput(x.KeyId, x.Amount, (HashSet<OutPoint>)[x.OutPoint]))
			.ToHashSet();

		foreach (var virtualOutput in walletVirtualOutputs)
		{
			(double standard, double sanctioned) startingOutputAnonset;

			// If the virtual output has a nonempty anonymity set
			if (!foreignVirtualOutputs.Any(x => x.Amount == virtualOutput.Amount))
			{
				// When WW2 denom output isn't too large, then it's not change.
				if (tx.IsWasabi2Cj is true && StdDenoms.Contains(virtualOutput.Amount.Satoshi))
				{
					if (maxAmountWeightedAverageIsApplicableFor is null && !TryGetLargestEqualForeignOutputAmount(foreignVirtualOutputs, out maxAmountWeightedAverageIsApplicableFor))
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
			double anonymityGain = Math.Min(CoinjoinAnalyzer.ComputeAnonymityContribution(virtualOutput.Coins.First(), walletId), foreignInputCount);

			// Account for the inherited anonymity set size from the inputs in the
			// anonymity set size estimate.
			double anonset = new[] { startingOutputAnonset.sanctioned + anonymityGain, anonymityGain + 1, startingOutputAnonset.standard }.Max();

			foreach (var coin in virtualOutput.Coins)
			{
				coin.AnonymitySet = anonset;
			}
		}
	}

	private static bool TryGetLargestEqualForeignOutputAmount(IEnumerable<ForeignVirtualOutput> foreignVirtualOutputs, [NotNullWhen(true)] out long? largestEqualForeignOutputAmount)
	{
		var found = foreignVirtualOutputs
			.Select(x => x.Amount.Satoshi)
			.GroupBy(x => x)
			.ToDictionary(x => x.Key, y => y.Count())
			.Select(x => (x.Key, x.Value))
			.Where(x => x.Value > 1)
			.FirstOrDefault().Key;

		largestEqualForeignOutputAmount = found == default ? null : found;

		return largestEqualForeignOutputAmount is not null;
	}

	/// <summary>
	/// Adjusts the anonset of the inputs to the newly calculated output anonsets.
	/// </summary>
	// private static void AdjustWalletInputs(DumbTransaction tx, WalletId walletId, double startingOutputAnonset)
	// {
	// 	// Sanity check.
	// 	if (tx.WalletOutputs.Count == 0)
	// 	{
	// 		return;
	// 	}

	// 	var smallestOutputAnonset = tx.WalletOutputs.Min(x => x.HdPubKey.AnonymitySet);
	// 	if (smallestOutputAnonset < startingOutputAnonset)
	// 	{
	// 		foreach (var key in tx.WalletVirtualInputs.Select(x => x.HdPubKey))
	// 		{
	// 			key.SetAnonymitySet(smallestOutputAnonset);
	// 		}
	// 	}
	// }
}
