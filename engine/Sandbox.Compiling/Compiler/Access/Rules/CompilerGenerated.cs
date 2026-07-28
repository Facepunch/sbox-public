namespace Sandbox;

internal static partial class Rules
{
	internal static string[] CompilerGenerated = new[]
	{
		// Only source is analysed, so members the compiler synthesises without the user naming them
		// (Span indexers, collection expressions, inline array buffers) need no rule here.
		//
		// Don't re-add allow entries for Unsafe.* or InlineArray* - they're hard blocked in Rules, and
		// IsBlocked runs first, so the entries would be dead while reading as permission.
		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.SizeOf<T>()",

		"System.Private.CoreLib/System.Runtime.CompilerServices.DecimalConstantAttribute",

		"System.Private.CoreLib/System.Runtime.CompilerServices.IAsyncStateMachine*",
	};
}
