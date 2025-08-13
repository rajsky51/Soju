using NBitcoin;
using Soju.WabiSabi.Models;
using WabiSabi.Crypto.Randomness;
using InsecureRandom = Soju.Crypto.Randomness.InsecureRandom;

namespace Soju.Extensions;

public static class MyExtensions
{
    // NOTE: Range is inclusive of both min and max
    public static Money GetRandomMoney(InsecureRandom rng, MoneyRange range)
    {
        return new Money(rng.GetInt64(range.Min.Satoshi, range.Max.Satoshi + 1));
    }
}
