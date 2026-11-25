using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// The CPU time the calling thread has run for, as opposed to the wall time it took: the main thread's and the render
/// thread's counters (<c>Borealis main thread CPU</c>, <c>Borealis render thread CPU</c>) say how much of a frame each spent
/// working rather than waiting. From the thread's cycle count on Windows (at the processor's rated clock, which the cycle
/// counter runs at), the thread's CPU clock elsewhere; 0 where neither is there.
/// </summary>
internal static class ThreadCpuTime
{
	/// <summary>The calling thread's CPU time so far, in milliseconds.</summary>
	public static double NowMs()
	{
		try
		{
			if ( OperatingSystem.IsWindows() )
			{
				if ( !QueryThreadCycleTime( GetCurrentThread(), out var cycles ) ) return 0;
				return cycles / CyclesPerMs;
			}

			if ( OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() )
			{
				// CLOCK_THREAD_CPUTIME_ID: 3 on Linux, 16 on macOS
				if ( clock_gettime( OperatingSystem.IsLinux() ? 3 : 16, out var spec ) != 0 ) return 0;
				return spec.Seconds * 1000.0 + spec.Nanoseconds / 1_000_000.0;
			}
		}
		catch ( Exception e ) when ( e is DllNotFoundException or EntryPointNotFoundException )
		{
		}

		return 0;
	}

	// The processor's rated clock in cycles per millisecond, which Windows' thread cycle counts run at (the invariant TSC)
	static double CyclesPerMs => cyclesPerMs ??= RatedMhz() * 1000.0;
	static double? cyclesPerMs;

	static double RatedMhz()
	{
		if ( OperatingSystem.IsWindows() && Microsoft.Win32.Registry.GetValue( @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", null ) is int mhz && mhz > 0 ) return mhz;
		return 3000;
	}

	[DllImport( "kernel32.dll" )]
	static extern nint GetCurrentThread();

	[DllImport( "kernel32.dll" )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool QueryThreadCycleTime( nint thread, out ulong cycles );

	[StructLayout( LayoutKind.Sequential )]
	struct TimeSpec
	{
		public long Seconds;
		public long Nanoseconds;
	}

	[DllImport( "libc", EntryPoint = "clock_gettime" )]
	static extern int clock_gettime( int clock, out TimeSpec spec );
}
