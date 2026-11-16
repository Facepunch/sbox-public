using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox.BorealisLab;

/// <summary>
/// Tracy for map runs, which start nothing of the engine: with <c>-tracy</c>, tier0's profiler starts here and Borealis's zones
/// go to it through Tracy's C API (<see cref="LabBackendRenderHost"/>), as the game's host sends them through the engine. On
/// demand: nothing is kept until a capture connects (<c>TRACY_PORT</c> picks the port).
/// </summary>
internal static unsafe class LabTracy
{
	[StructLayout( LayoutKind.Sequential )]
	struct SourceLocation
	{
		public IntPtr Name, Function, File;
		public uint Line, Color;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct ZoneContext
	{
		public uint Id;
		public int Active;
	}

	static delegate* unmanaged<SourceLocation*, int, ZoneContext> zoneBegin;
	static delegate* unmanaged<ZoneContext, void> zoneEnd;
	static delegate* unmanaged<IntPtr, void> frameMark;

	/// <summary>Whether the profiler started.</summary>
	public static bool Enabled { get; private set; }

	/// <summary>A source location per zone name, made once.</summary>
	static readonly ConcurrentDictionary<(IntPtr Name, uint Color), IntPtr> locations = new();

	[ThreadStatic] static Stack<ZoneContext> open;

	static readonly IntPtr FrameName = Marshal.StringToCoTaskMemUTF8( "Borealis Lab Frame" );
	static readonly IntPtr Function = Marshal.StringToCoTaskMemUTF8( "Borealis" );

	/// <summary>Start tier0's profiler, once, if <c>-tracy</c> asks for it.</summary>
	public static void Start()
	{
		if ( Enabled || !Args.Has( "-tracy" ) ) return;

		var path = Path.Combine( Environment.CurrentDirectory, "bin", "win64", "tier0.dll" );
		if ( !NativeLibrary.TryLoad( path, out var tier0 ) )
		{
			Console.Error.WriteLine( $"-tracy: no {path}" );
			return;
		}

		var startup = (delegate* unmanaged<void>)NativeLibrary.GetExport( tier0, "___tracy_startup_profiler" );
		zoneBegin = (delegate* unmanaged<SourceLocation*, int, ZoneContext>)NativeLibrary.GetExport( tier0, "___tracy_emit_zone_begin" );
		zoneEnd = (delegate* unmanaged<ZoneContext, void>)NativeLibrary.GetExport( tier0, "___tracy_emit_zone_end" );
		frameMark = (delegate* unmanaged<IntPtr, void>)NativeLibrary.GetExport( tier0, "___tracy_emit_frame_mark" );
		startup();
		Enabled = true;
	}

	public static void Begin( IntPtr name, uint color )
	{
		var location = locations.GetOrAdd( (name, color), static key =>
		{
			var made = (SourceLocation*)NativeMemory.AllocZeroed( (nuint)sizeof( SourceLocation ) );
			*made = new SourceLocation { Name = key.Name, Function = Function, File = Function, Color = key.Color >> 8 };
			return (IntPtr)made;
		} );

		(open ??= new()).Push( zoneBegin( (SourceLocation*)location, 1 ) );
	}

	public static void End()
	{
		if ( open is { Count: > 0 } ) zoneEnd( open.Pop() );
	}

	/// <summary>A lab frame: a zone, and Tracy's frame mark after it.</summary>
	public static FrameScope Frame()
	{
		if ( !Enabled ) return default;
		Begin( FrameName, 0xFFFFFFFF );
		return new FrameScope( true );
	}

	public readonly struct FrameScope( bool open ) : IDisposable
	{
		public void Dispose()
		{
			if ( !open ) return;
			End();
			frameMark( IntPtr.Zero );
		}
	}
}

/// <summary>
/// The lab's Borealis.Gpu host: with <c>-tracy</c>, Borealis's profiler zones go into Tracy (<see cref="LabTracy"/>).
/// </summary>
internal sealed class LabBackendRenderHost( Borealis.Backend.BackendDevice device ) : Borealis.Backend.BackendRenderHost( device )
{
	public override bool ProfilesZones => LabTracy.Enabled;

	public override void BeginZone( IntPtr name, uint color ) => LabTracy.Begin( name, color );

	public override void EndZone() => LabTracy.End();
}
