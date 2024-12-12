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
}
