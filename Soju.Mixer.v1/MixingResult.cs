using NBitcoin;

namespace Soju;

public class MixingInput()
{
	public string Address;
	public uint256 TxId;
	public Int64 Value;
	
	public string? WalletName;
	public double AnonScore;
}

public class MixingOutput()
{
	public string Address;
	public Int64 Value;
	public bool IsStdDenom;
	
	public string? WalletName;
	public double AnonScore;
}

public record MixingResult(
	MixingInput[] Inputs,
	MixingOutput[] Outputs,
	uint256 TxId,
	uint256 RoundId,
	int RelativeOrder);
