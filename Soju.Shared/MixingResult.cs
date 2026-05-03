using System.Text.Json.Nodes;

namespace Soju;

public record struct MixingInput
(
	string Address,
	string TxId,
	Int64 Value,
	string WalletName,
	double AnonScore
);

public record struct MixingOutput
(
	string Address,
	Int64 Value,
	bool IsStdDenom,
	string WalletName,
	double AnonScore
);

public record MixingResult
(
	MixingInput[] Inputs,
	MixingOutput[] Outputs,
	string TxId,
	string RoundId,
	int TransactionVirtualSize
);

public class MixingResultJsonSerializer {
	public static JsonObject BuildCoinJoinJson(MixingResult result, int relativeOrder)
	{
		var inputsObj = new JsonObject();

		for (int i = 0; i < result.Inputs.Length; i++)
		{
			var input = result.Inputs[i];

			inputsObj[i.ToString()] = new JsonObject
			{
				["address"] = input.Address,
				["txid"] = input.TxId,
				["value"] = input.Value,
				["wallet_name"] = input.WalletName,
				["anon_score"] = input.AnonScore
			};
		}

		var outputsObj = new JsonObject();

		for (int i = 0; i < result.Outputs.Length; i++)
		{
			var output = result.Outputs[i];

			outputsObj[i.ToString()] = new JsonObject
			{
				["address"] = output.Address,
				["value"] = output.Value,
				["is_std_denom"] = output.IsStdDenom,
				["wallet_name"] = output.WalletName,
				["anon_score"] = output.AnonScore
			};
		}

		var txObj = new JsonObject
		{
			["relative_order"] = relativeOrder,
			["inputs"] = inputsObj,
			["outputs"] = outputsObj
		};

		var coinjoinsObj = new JsonObject
		{
			[result.TxId] = txObj
		};

		var root = new JsonObject
		{
			["coinjoins"] = coinjoinsObj
		};

		return root;
	}
}
