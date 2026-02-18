using NBitcoin;
using NBitcoin.RPC;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Soju.BitcoinRpc;
using Soju.Blockchain.Analysis.FeesEstimation;
using Soju.Helpers;
using Soju.Logging;
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
	
	private static EstimateSmartFeeResponse SimulateRegTestFeeEstimation(this IRPCClient rpc, int confirmationTarget)
	{
		var resp = new EstimateSmartFeeResponse {Blocks = confirmationTarget, FeeRate = rpc.GetCurrentMiningFeeRate()};
		return resp;
	}
	
	private static FeeRateByConfirmationTarget SimulateRegTestFeeEstimation(this IRPCClient rpc) =>
		Constants.ConfirmationTargets
		.Select(target => rpc.SimulateRegTestFeeEstimation(target))
		.ToDictionary(x => x.Blocks, x => (int)Math.Ceiling(x.FeeRate.SatoshiPerByte));
	
	public static AllFeeEstimate EstimateAllFee(this IRPCClient rpc)
	{
		Debug.Assert(rpc.Network == Network.RegTest);
		return new AllFeeEstimate(rpc.SimulateRegTestFeeEstimation());
	}
}