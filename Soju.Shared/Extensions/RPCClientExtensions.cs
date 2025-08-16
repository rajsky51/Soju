using NBitcoin;
using NBitcoin.RPC;
using System.Diagnostics;
using Soju.BitcoinCore.Rpc;
using Soju.Blockchain.Analysis.FeesEstimation;
using Soju.Helpers;
using FeeRateByConfirmationTarget = System.Collections.Generic.Dictionary<int, int>;

namespace Soju.Extensions;

public static class RPCClientExtensions
{
	public static EstimateSmartFeeResponse EstimateConservativeSmartFee(this IRPCClient rpc, int confirmationTarget)
	{
		var estimations = rpc.EstimateAllFee();
		return new EstimateSmartFeeResponse
		{
			Blocks = confirmationTarget,
			FeeRate = estimations.GetFeeRate(confirmationTarget)
		};
	}
	
	private static EstimateSmartFeeResponse SimulateRegTestFeeEstimation(int confirmationTarget)
	{
		// TODO: We should be able to calculate this based on feeRate given in scenario
		int satoshiPerByte = (Constants.SevenDaysConfirmationTarget + 1 + 6 - confirmationTarget) / 7;
		Money feePerK = Money.Satoshis(satoshiPerByte * 1000);
		FeeRate feeRate = new(feePerK);
		var resp = new EstimateSmartFeeResponse { Blocks = confirmationTarget, FeeRate = feeRate };
		return resp;
	}
	
	private static FeeRateByConfirmationTarget SimulateRegTestFeeEstimation() =>
		Constants.ConfirmationTargets
		.Select(target => SimulateRegTestFeeEstimation(target))
		.ToDictionary(x => x.Blocks, x => (int)Math.Ceiling(x.FeeRate.SatoshiPerByte));
	
	public static AllFeeEstimate EstimateAllFee(this IRPCClient rpc)
	{
		Debug.Assert(rpc.Network == Network.RegTest);
		return new AllFeeEstimate(SimulateRegTestFeeEstimation());
	}
}