namespace Sandbox;

internal static partial class Rules
{
	internal static string[] CompilerGenerated = new[]
	{
		// Compiler generates all this scary shit that the user shouldn't be using
		// User code is checked in Sandbox.Compiling blacklist.
		//
		// These are the ref-based Unsafe members the C# compiler emits for safe code
		// (Span indexers, inline arrays, collection expressions, in/ref readonly). We list them
		// explicitly rather than with Add*/As*/AsRef* wildcards so we DON'T expose the three
		// pointer-bridging overloads, which are memory-safety escape hatches and are never emitted
		// for non-unsafe code: Unsafe.Add<T>(void*,int), Unsafe.AsPointer<T>(ref T)->void*,
		// and Unsafe.AsRef<T>(void*)->ref T.
		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.Add<T>( T&, System.Int32 )",
		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.Add<T>( T&, System.IntPtr )",
		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.Add<T>( T&, System.UIntPtr )",
		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.AddByteOffset<T>( T&, System.IntPtr )",
		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.AddByteOffset<T>( T&, System.UIntPtr )",
		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.As<T>( System.Object )",
		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.As<TFrom,TTo>( TFrom& )",
		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.AsRef<T>( T& )",

		"System.Private.CoreLib/System.Runtime.CompilerServices.Unsafe.SizeOf<T>()",

		// Compiler-emitted inline-array buffers backing collection expressions and
		// `params ReadOnlySpan<T>` call sites. The runtime shares these across assemblies.
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray2`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray3`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray4`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray5`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray6`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray7`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray8`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray9`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray10`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray11`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray12`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray13`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray14`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray15`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArray16`1",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArrayAttribute",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArrayAttribute..ctor( System.Int32 )",
		"System.Private.CoreLib/System.Runtime.CompilerServices.InlineArrayAttribute.get_Length()",
		"System.Private.CoreLib/System.Runtime.CompilerServices.DecimalConstantAttribute",
		"System.Private.CoreLib/System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan*",

		"System.Private.CoreLib/System.Runtime.CompilerServices.IAsyncStateMachine*",
	};
}
