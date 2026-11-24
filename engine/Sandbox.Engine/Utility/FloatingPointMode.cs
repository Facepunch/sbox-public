using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// A thread's floating-point mode: how it treats denormals and how it rounds. The engine's native threads and the main
/// thread flush denormals to zero, threads the runtime starts don't, and the same sum can come out a bit different in each.
/// <see cref="JobDispatcher"/> runs a job in its caller's mode, so a result doesn't depend on which thread worked it out.
/// </summary>
[SkipHotload]
internal static partial class FloatingPointMode
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
	internal static uint Current => Supported ? ControlFp( 0, 0 ) & Mask : 0;

	/// <summary>
	/// Put this thread in <paramref name="mode"/>, a value of <see cref="Current"/>.
	/// </summary>
	internal static void Set( uint mode )
	{
		if ( !Supported ) return;
		if ( (ControlFp( 0, 0 ) & Mask) == mode ) return;

		ControlFp( mode, Mask );
	}

	[LibraryImport( "ucrtbase.dll", EntryPoint = "_controlfp" )]
	[SuppressGCTransition]
	private static partial uint ControlFp( uint value, uint mask );
}
