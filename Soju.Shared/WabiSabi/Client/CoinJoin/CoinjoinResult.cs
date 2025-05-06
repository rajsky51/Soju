using System.Text.Json.Serialization;
using NBitcoin;
using Soju.Blockchain.Transactions;
using Soju.Json;

namespace Soju.WabiSabi.Client.CoinJoin;

[JsonConverter(typeof(CoinjoinResultConverter))]
public record CoinjoinResult 
{
    public readonly uint256 RoundId;
    public readonly DumbTransaction Transaction;
    public readonly Money MiningFee;
    public readonly Money CoordinationFee;

    public CoinjoinResult(DumbTransaction transaction, uint256 roundId, Money miningFee, Money coordinationFee)
    {
        Transaction = transaction;
        RoundId = roundId;
        MiningFee = miningFee;
        CoordinationFee = coordinationFee;
    }
}