using System.Text.Json.Serialization;
using NBitcoin;
using Soju.Json;

namespace Soju;

[JsonConverter(typeof(CoinjoinResultConverter))]
public record CoinjoinResult 
{
    public readonly DumbTransaction Transaction;
    public readonly uint256 RoundId;

    public CoinjoinResult(DumbTransaction transaction, uint256 roundId)
    {
        Transaction = transaction;
        RoundId = roundId;
    }
}