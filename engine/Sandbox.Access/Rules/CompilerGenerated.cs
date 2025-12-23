namespace Sandbox;

internal static partial class Rules
{
	internal static string[] CompilerGenerated = new[]
	{
		// Compiler generates all this scary shit that the user shouldn't be using
		// User code is checked in Sandbox.Compiling blacklist
		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.Add*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.As*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.AsRef*",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArrayAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.DecimalConstantAttribute",
		"System.Private.CoreLib/System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan*",

		"System.Private.CoreLib/System.Runtime.CompilerServices.IAsyncStateMachine*",
	};
}
