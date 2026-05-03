using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Soju.Diagnostics;

public static class Fail
{
	[DebuggerHidden]
	[DoesNotReturn]
	public static void Unreachable(string message = "",
		[CallerFilePath]   string file = "",
		[CallerLineNumber] int    line = 0,
		[CallerMemberName] string member = "")
	{
		Debugger.Break();
		throw new UnreachableException(
			$"Unreachable code hit in {member} ({file}:{line}): {message}");
	}
}