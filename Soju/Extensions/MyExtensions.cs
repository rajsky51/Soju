using NBitcoin;
using WabiSabi.Crypto.Randomness;
using InsecureRandom = Soju.Randomness.InsecureRandom;

namespace Soju.Extensions;

public static class MyExtensions
{
    public static ulong Sum(this IEnumerable<ulong> me)
    {
        ulong inputSum = 0;
        foreach (var item in me)
        {
            inputSum += item;
        }
        return inputSum;
    }

    public static T[] RandomElements<T>(this IEnumerable<T> list, int elementsCount)
    {
        return list.OrderBy(arg => Guid.NewGuid()).Take(elementsCount).ToArray();
    }

    public static HashSet<T> MakeHashSet<T>(this T o)
    {
        HashSet<T>hs = [o];
        return hs;
    }

    // Range is inclusive of both min and max
    public static Money GetMoney(this InsecureRandom rng, MoneyRange range)
    {
        return new Money(rng.GetInt64(range.Min.Satoshi, range.Max.Satoshi + 1));
    }

    public static bool GetBool(this WasabiRandom rng)
    {
        return rng.GetInt(0, 2) == 0;
    }
}
