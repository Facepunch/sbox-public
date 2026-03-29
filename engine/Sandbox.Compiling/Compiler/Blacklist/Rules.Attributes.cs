namespace Sandbox;

static partial class CompilerRules
{
	public static readonly List<string> Attributes =
	[
		"System.Runtime.CompilerServices.InlineArrayAttribute*",
		"System.Runtime.CompilerServices.ExtensionMarkerAttribute",
		"System.Runtime.CompilerServices.ParamCollectionAttribute",

		// Can be used to read uninitialized stack memory.
		"System.Runtime.CompilerServices.SkipLocalsInitAttribute*",

		// All of these can potentially lead to RCEs
		"System.Runtime.CompilerServices.UnsafeAccessorAttribute*",
		"System.Runtime.CompilerServices.UnsafeAccessorTypeAttribute*",
		"System.Runtime.CompilerServices.AsyncMethodBuilderAttribute*",
	];
}
