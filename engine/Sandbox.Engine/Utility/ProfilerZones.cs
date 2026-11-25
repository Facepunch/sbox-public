using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Sandbox.Utility;

/// <summary>
/// Where the engine's and Borealis's profiler zones go: the engine's native profiler (<c>PerformanceTrace</c>), or with
/// <c>-tracy</c> a Tracy client, <c>bin/win64/TracyClient.dll</c> (<c>bin/linuxsteamrt64/libTracyClient.so</c> on Linux), which the
/// shipped tier0 doesn't carry. The client is
/// built on demand (connect a Tracy profiler of the same version to see it).
/// </summary>
internal static unsafe class ProfilerZones
{
	[StructLayout( LayoutKind.Sequential )]
	struct SourceLocation
	{
		public IntPtr Name, Function, File;
		public uint Line, Color;
	}

	// TracyCZoneCtx of a TRACY_ON_DEMAND client: 16 bytes, so returned and passed through memory, not registers
	[StructLayout( LayoutKind.Sequential )]
	struct ZoneContext
	{
		public uint Id;
		public int Active;
		public ulong ConnectionId;
	}

	static readonly delegate* unmanaged< SourceLocation*, int, ZoneContext > zoneBegin;
	static readonly delegate* unmanaged< ZoneContext, void > zoneEnd;
	static readonly delegate* unmanaged< IntPtr, void > frameMark;

	/// <summary>Whether zones go to Tracy.</summary>
	public static bool Tracy { get; }

	/// <summary>Whether zones go anywhere: Tracy, or a native profiler tier0 found.</summary>
	public static bool Enabled => Tracy || NativeEngine.PerformanceTrace.__N.PerformanceTrace_BeginEvent != null;

	// A source location per zone name and colour, made once: Tracy keeps the pointer
	static readonly ConcurrentDictionary<(IntPtr Name, uint Color), IntPtr> locations = new();
	static readonly IntPtr Function = Marshal.StringToCoTaskMemUTF8( "s&box" );

	[ThreadStatic] static Stack<ZoneContext> open;

	static ProfilerZones()
	{
		if ( !CommandLine.HasSwitch( "-tracy" ) ) return;

		// Loading the client starts it
		// Next to the engine's native libraries (the game), or a folder up from bin/managed (the labs)
		var (folder, file) = OperatingSystem.IsWindows() ? ("win64", "TracyClient.dll") : ("linuxsteamrt64", "libTracyClient.so");
		var path = System.IO.Path.Combine( AppContext.BaseDirectory, NetCore.NativeDllPath, file );
		var labPath = System.IO.Path.Combine( AppContext.BaseDirectory, "..", folder, file );
		if ( !NativeLibrary.TryLoad( path, out var client ) && !NativeLibrary.TryLoad( labPath, out client ) && !NativeLibrary.TryLoad( file, out client ) )
		{
			Log.Warning( $"-tracy: no {file} at {path}" );
			return;
		}

		zoneBegin = (delegate* unmanaged< SourceLocation*, int, ZoneContext >)NativeLibrary.GetExport( client, "___tracy_emit_zone_begin" );
		zoneEnd = (delegate* unmanaged< ZoneContext, void >)NativeLibrary.GetExport( client, "___tracy_emit_zone_end" );
		frameMark = (delegate* unmanaged< IntPtr, void >)NativeLibrary.GetExport( client, "___tracy_emit_frame_mark" );
		Tracy = true;
	}

	/// <summary>Open a zone on this thread: a UTF-8 name that lives as long as the process, and an RGBA colour.</summary>
	public static void Begin( IntPtr name, uint color, string data = null, string file = null, int line = 0, string function = null )
	{
		if ( !Tracy )
		{
			if ( NativeEngine.PerformanceTrace.__N.PerformanceTrace_BeginEvent != null ) NativeEngine.PerformanceTrace.BeginEvent( name, data, color, file, line, function );
			return;
		}

		var location = locations.GetOrAdd( (name, color), static key =>
		{
			var made = (SourceLocation*)NativeMemory.AllocZeroed( (nuint)sizeof( SourceLocation ) );
			*made = new SourceLocation { Name = key.Name, Function = Function, File = Function, Color = key.Color >> 8 };
			return (IntPtr)made;
		} );

		(open ??= new()).Push( zoneBegin( (SourceLocation*)location, 1 ) );
	}

	/// <summary>Close the zone <see cref="Begin"/> opened on this thread.</summary>
	public static void End()
	{
		if ( !Tracy )
		{
			if ( NativeEngine.PerformanceTrace.__N.PerformanceTrace_BeginEvent != null ) NativeEngine.PerformanceTrace.EndEvent();
			return;
		}

		if ( open is { Count: > 0 } ) zoneEnd( open.Pop() );
	}

	/// <summary>The end of an engine frame, Tracy's frame mark.</summary>
	public static void FrameMark()
	{
		if ( Tracy ) frameMark( IntPtr.Zero );
	}
}
