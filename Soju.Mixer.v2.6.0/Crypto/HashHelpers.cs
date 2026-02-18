using System.Security.Cryptography;
using System.Text;
using Soju.Helpers;

namespace Soju.Crypto;

public static class HashHelpers
{
	public static int ComputeHashCode(params byte[] data)
	{
		var hash = new HashCode();
		foreach (var element in data)
		{
			hash.Add(element);
		}
		return hash.ToHashCode();
	}
}
