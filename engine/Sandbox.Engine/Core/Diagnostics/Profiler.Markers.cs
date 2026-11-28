using System.IO;
using System.Text;
using Native = NativeEngine.Profiler;

namespace Sandbox.Diagnostics;

/// <summary>
/// Warnings and errors logged while a capture records, as markers at their time and thread.
/// </summary>
internal static partial class Profiler
{
	/// <summary>
	/// A logged warning or error, <paramref name="ThreadKey"/> 0 when its thread recorded no scopes.
	/// </summary>
	internal sealed record LogMarker( double Ms, ulong ThreadKey, LogLevel Level, string Text );

	readonly record struct RawMarker( ulong Ticks, ulong Thread, LogLevel Level, string Text );

	internal const int MaxMarkers = 1000;
	const int MaxMarkerText = 256;
	static bool _listeningToLogs;

	static void ListenToLogs()
	{
		if ( _listeningToLogs ) return;
		_listeningToLogs = true;
		Logging.OnEmitted += OnLogEmitted;
	}

	// Runs on the thread that logged
	static void OnLogEmitted( LogEvent e )
	{
		if ( e.Level < LogLevel.Warn || _builder is not { } builder ) return;
		var ticks = Native.Now();
		lock ( builder.Markers )
		{
			if ( builder.Markers.Count >= MaxMarkers ) { builder.MarkersLost++; return; }
			var text = e.Message ?? e.Exception?.Message ?? "";
			if ( text.Length > MaxMarkerText ) text = text[..MaxMarkerText];
			builder.Markers.Add( new( ticks, Native.ThreadId(), e.Level, text ) );
		}
	}

	static LogMarker[] ResolveMarkers( Capture capture, List<RawMarker> raw, ulong start, ulong end, double toMs )
	{
		var byOsThread = capture.Threads.Where( x => x.Key != GcThreadKey ).ToLookup( x => x.Id );
		return raw.Where( x => x.Ticks >= start && x.Ticks <= end ).OrderBy( x => x.Ticks ).Select( x =>
		{
			var ms = ((double)x.Ticks - start) * toMs;
			return new LogMarker( ms, FindThread( capture, byOsThread[x.Thread], ms ), x.Level, x.Text );
		} ).ToArray();
	}

	static LogMarker[] RelocateMarkers( Capture capture, LogMarker[] markers )
	{
		var byOsThread = capture.Threads.Where( x => x.Key != GcThreadKey && !IsGpuLane( x.Key ) ).ToLookup( x => x.Id );
		return markers.Select( x => capture.Threads.FirstOrDefault( t => t.Key == x.ThreadKey ) is { } thread
			? x with { ThreadKey = FindThread( capture, byOsThread[thread.Id], x.Ms ) } : x ).ToArray();
	}

	static void WriteMarkers( BinaryWriter writer, Capture capture )
	{
		writer.Write( capture.Markers.Length );
		writer.Write( capture.MarkersLost );
		foreach ( var marker in capture.Markers )
		{
			writer.Write( marker.Ms ); writer.Write( marker.ThreadKey ); writer.Write( (int)marker.Level ); WriteText( writer, marker.Text );
		}
	}

	static (LogMarker[] Markers, int Lost) ReadMarkers( Stream stream, double offsetMs = 0, double durationMs = double.PositiveInfinity )
	{
		using var reader = new BinaryReader( stream, Encoding.UTF8, leaveOpen: true );
		var count = reader.ReadInt32();
		var lost = reader.ReadInt32();
		if ( count is < 0 or > MaxMarkers || lost < 0 ) throw new InvalidDataException( "Invalid profiler capture log markers." );
		var markers = new List<LogMarker>( count );
		for ( int i = 0; i < count; i++ )
		{
			var ms = reader.ReadDouble(); var thread = reader.ReadUInt64(); var level = reader.ReadInt32(); var text = ReadText( reader );
			if ( !double.IsFinite( ms ) || level is < 0 or > (int)LogLevel.Error ) throw new InvalidDataException( "Invalid profiler capture log marker." );
			if ( ms < offsetMs || ms > offsetMs + durationMs ) continue;
			markers.Add( new( ms - offsetMs, thread, (LogLevel)level, text ) );
		}
		return (markers.ToArray(), lost);
	}
}
