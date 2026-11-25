using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// A thread's floating-point mode: how it treats denormals and how it rounds. The engine's native threads and the main
/// thread flush denormals to zero, threads the runtime starts don't, and the same sum can come out a bit different in each.
/// <see cref="JobDispatcher"/> runs a job in its caller's mode, so a result doesn't depend on which thread worked it out.
/// </summary>
[SkipHotload]
internal static unsafe class FloatingPointMode
{
	/// <summary>
	/// Denormal inputs and results are flushed to zero (<c>_DN_FLUSH</c>).
	/// </summary>
	internal const uint FlushDenormals = 0x01000000;

	// _MCW_DN | _MCW_RC: denormal handling and rounding. Exception masks stay as each thread has them.
	const uint Mask = 0x03000000 | 0x00000300;

	static readonly bool Supported = OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.X86;

	/// <summary>
	/// This thread's mode, or 0 where it isn't read (outside Windows on x86 and x64).
	/// </summary>
	internal static uint Current => Supported ? Read() & Mask : 0;

	/// <summary>
	/// Put this thread in <paramref name="mode"/>, a value of <see cref="Current"/>.
	/// </summary>
	internal static void Set( uint mode )
	{
		if ( !Supported ) return;
		if ( (Read() & Mask) == mode ) return;

		Write( mode, Mask );
	}

	// _controlfp is called through a pointer bound on first use, not a P/Invoke: a P/Invoke without a GC transition is bound
	// when its caller is compiled, so precompiling the callers (PreJIT, on every start) failed on Linux, where there's no
	// ucrtbase.dll, even though nothing calls them there.
	static delegate* unmanaged[Cdecl, SuppressGCTransition]<uint, uint, uint> controlFp;

	static uint Read() => ControlFp( 0, 0 );

	static void Write( uint value, uint mask ) => ControlFp( value, mask );

	static uint ControlFp( uint value, uint mask )
	{
		var fn = controlFp;
		if ( fn == null )
		{
			var library = NativeLibrary.Load( "ucrtbase.dll", typeof( FloatingPointMode ).Assembly, null );
			controlFp = fn = (delegate* unmanaged[Cdecl, SuppressGCTransition]<uint, uint, uint>)NativeLibrary.GetExport( library, "_controlfp" );
		}

		return fn( value, mask );
	}
}
