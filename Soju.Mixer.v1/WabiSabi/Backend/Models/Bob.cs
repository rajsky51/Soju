using NBitcoin;
using Soju.Extensions;

namespace Soju.WabiSabi.Backend.Models;

/// <param name="CredentialAmount"> This is slightly larger than the final TXO amount,because the fees are coming down from this.</param>
public record Bob(ScriptType ScriptType, long CredentialAmount)
{
    public int OutputVsize
        => ScriptType.EstimateOutputVsize();

    public Money CalculateOutputAmount(FeeRate feeRate)
        => CredentialAmount - feeRate.GetFee(OutputVsize);
}