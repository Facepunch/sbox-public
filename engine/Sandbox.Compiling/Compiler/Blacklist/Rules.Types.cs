namespace Sandbox;

static partial class CompilerRules
{
	public static readonly List<string> Types =
	[
		// Compiler-emitted inline-array buffers backing collection expressions and
		// `params ReadOnlySpan<T>` call sites. The compiler is allowed to lower to these
		// (they're on the Access whitelist), but user code may not reference them directly.
		// Broad wildcard on purpose: this is a deny-list, so over-matching is safe and it
		// auto-covers any future InlineArrayN the runtime adds. Also re-covers
		// InlineArrayAttribute, which Rules.Attributes.cs already bans.
		"System.Runtime.CompilerServices.InlineArray*",
	];
}
