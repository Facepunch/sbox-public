namespace Sandbox;

// Prefixes: none = allow, ! = deny override, - = hard block, + = block carve-out.
internal static partial class Rules
{
	internal static string[] All =>
	[
		.. BaseAccess,
		.. Types,
		.. Numerics,
		.. Reflection,
		.. Exceptions,
		.. Diagnostics,
		.. Async,
		.. CompilerGenerated,

		"-System.Runtime.CompilerServices.Unsafe.*",
		"+System.Runtime.CompilerServices.Unsafe.SizeOf<*>()",
		"-System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan*",
		"-System.Runtime.InteropServices.MemoryMarshal.CreateSpan*",
		"-System.Span`1.GetPinnableReference()",
		"-System.ReadOnlySpan`1.GetPinnableReference()",

		// Backstop - whitelisted builds compile without /unsafe, so source can't name a pointer anyway.
		"-System.Span`1..ctor( System.Void*, System.Int32 )",
		"-System.ReadOnlySpan`1..ctor( System.Void*, System.Int32 )",
		"-System.Runtime.CompilerServices.InlineArray*",
		"-System.Runtime.CompilerServices.InlineArrayAttribute*",
		"-System.Runtime.CompilerServices.ExtensionMarkerAttribute",
		"-System.Runtime.CompilerServices.ParamCollectionAttribute",
		"-System.Runtime.CompilerServices.SkipLocalsInitAttribute*",
		"-System.Runtime.CompilerServices.UnsafeAccessorAttribute*",
		"-System.Runtime.CompilerServices.UnsafeAccessorTypeAttribute*",
		"-System.Runtime.CompilerServices.UnsafeAccessorKind",
		"-System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute*",
		"-System.Runtime.CompilerServices.ModuleInitializerAttribute*",
		"-System.Runtime.CompilerServices.InterceptsLocationAttribute*",
		"-System.Runtime.CompilerServices.AsyncMethodBuilderAttribute*",
	];
}
