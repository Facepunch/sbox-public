namespace Sandbox;

static partial class CompilerRules
{
	public static readonly List<string> Methods =
	[
		"System.Runtime.CompilerServices.Unsafe.*",
		// SizeOf<T>() just returns a type's byte size - no memory access, no type punning. Safe to write.
		// (Wildcard on the type arg because the symbol renders the constructed form, e.g. SizeOf<int>().)
		"!System.Runtime.CompilerServices.Unsafe.SizeOf<*>()",

		// Both of these create a span from a ref with an unchecked length -> out-of-bounds read/write.
		"System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan*",
		"System.Runtime.InteropServices.MemoryMarshal.CreateSpan*",

		// Allowed at an IL level because it's compiler-generated, disaster if acccessible
		"System.Span<*>.GetPinnableReference()",
		"System.ReadOnlySpan<*>.GetPinnableReference()",
	];
}
