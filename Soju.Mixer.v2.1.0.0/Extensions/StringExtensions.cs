using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static System.Text.RegularExpressions.Regex;

namespace Soju.Extensions;

public static partial class StringExtensions
{
	/// <summary>
	/// Removes one trailing occurrence of the specified string
	/// </summary>
	public static string TrimEnd(this string me, string trimString, StringComparison comparisonType)
	{
		if (me.EndsWith(trimString, comparisonType))
		{
			return me[..^trimString.Length];
		}
		return me;
	}
}
