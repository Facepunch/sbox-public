using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Sandbox.Diagnostics;

internal static partial class Profiler
{
	const ulong FileMagic = 0x3146505543425353;
	// Version 2 adds instance labels after the threads
	const int FileVersion = 2, SpanBytes = 48;
	static readonly SemaphoreSlim FileGate = new( 1 );
	internal static string CaptureDirectory => Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), "sbox", "profiles" );
	internal static bool FileBusy => FileGate.CurrentCount == 0;

	internal static string CapturePath( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) || name.Length > 128 || name.Any( c => !char.IsAsciiLetterOrDigit( c ) && c != '-' && c != '_' && c != '.' ) || name.StartsWith( '.' ) )
			throw new ArgumentException( "Use a capture filename containing letters, numbers, '-', '_' or '.'.", nameof( name ) );
		if ( !name.EndsWith( ".sprof", StringComparison.OrdinalIgnoreCase ) ) name += ".sprof";
		return Path.Combine( CaptureDirectory, name );
	}

	/// <summary>
	/// A saved capture's file name: when it was saved, and its ID.
	/// </summary>
	internal static string CaptureFileName( Capture capture ) => $"capture-{DateTime.Now:yyyyMMdd-HHmmss}-{capture.Id}.sprof";

	internal static string[] SavedCaptures() => Directory.Exists( CaptureDirectory )
		? Directory.GetFiles( CaptureDirectory, "*.sprof" ).OrderByDescending( File.GetLastWriteTimeUtc ).Select( Path.GetFileName ).ToArray() : [];

	static void BeginFileOperation()
	{
		ThreadSafe.AssertIsMainThread();
		if ( RecordingId != 0 ) throw new InvalidOperationException( "Wait for the profiler capture to finish before saving or loading." );
		if ( !FileGate.Wait( 0 ) ) throw new InvalidOperationException( "A profiler capture file operation is already running." );
	}

	internal static async Task<string> SaveCapture( Capture capture, string name )
	{
		ArgumentNullException.ThrowIfNull( capture );
		var path = CapturePath( name );
		BeginFileOperation();
		try
		{
			await Task.Run( () =>
			{
				Directory.CreateDirectory( CaptureDirectory );
				var temporary = path + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
				try
				{
					using ( var stream = new FileStream( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024 ) ) WriteCapture( capture, stream );
					File.Move( temporary, path, overwrite: false );
				}
				finally { if ( File.Exists( temporary ) ) File.Delete( temporary ); }
			} );
			return path;
		}
		finally { FileGate.Release(); }
	}

	internal static async Task<Capture> LoadCapture( string name, double startMs = 0, double durationMs = 0 )
	{
		var path = CapturePath( name );
		BeginFileOperation();
		try
		{
			// Checked before making room, which drops older captures
			if ( !File.Exists( path ) ) throw new FileNotFoundException( "No such profiler capture.", path );
			int limit;
			lock ( HistoryLock ) limit = ReserveInstances();
			if ( limit == 0 ) throw new InvalidOperationException( "The captures open in the devtools profiler use the whole memory budget. Increase profiler_memory_mb." );
			return await Task.Run( () =>
			{
				using var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan );
				var capture = ReadCapture( stream, limit, startMs, durationMs ) with { Id = Interlocked.Increment( ref _nextId ), SourceFile = Path.GetFileName( path ) };
				Publish( capture );
				return capture;
			} );
		}
		finally { FileGate.Release(); }
	}

	internal static void WriteCapture( Capture capture, Stream stream )
	{
		using var writer = new BinaryWriter( stream, Encoding.UTF8, true );
		WriteContainerHeader( writer );
		var sections = new List<FileSection> { WriteSection( writer, SnapshotSection, () => WriteCapturePayload( capture, stream ) ) };
		// Written whenever GC data was recorded, so a capture with no samples still loads as having it
		if ( capture.HasGcData || capture.Allocations.Length > 0 || capture.Frames.Any( x => x.AllocatedBytes is not null ) )
			sections.Add( WriteSection( writer, MemorySection, () => WriteMemoryPayload( capture, writer ), version: 2 ) );
		WriteTracks( writer, capture, sections );
		WriteDirectory( writer, [.. sections] );
	}

	/// <summary>
	/// Version 2: type names once, allocation samples with their times, then each frame's main thread allocations in
	/// frame order (-1 when not measured). Version 1 had a name per sample and no times.
	/// </summary>
	static void WriteMemoryPayload( Capture capture, BinaryWriter writer )
	{
		var types = capture.Allocations.Select( x => x.Type ).Distinct().ToArray();
		var typeIndex = types.Select( ( x, i ) => (x, i) ).ToDictionary( x => x.x, x => x.i );
		writer.Write( types.Length );
		foreach ( var type in types ) WriteText( writer, type );
		writer.Write( capture.Allocations.Length );
		foreach ( var sample in capture.Allocations )
		{
			writer.Write( typeIndex[sample.Type] ); writer.Write( sample.ThreadKey ); writer.Write( sample.ScopeId ); writer.Write( sample.Samples ); writer.Write( sample.Bytes ); writer.Write( sample.TimeMs );
		}

		writer.Write( capture.Frames.Length );
		foreach ( var frame in capture.Frames )
			writer.Write( frame.AllocatedBytes ?? -1 );
		writer.Write( capture.AllocationCutoffMs ?? double.NaN );
	}

	static Capture ReadMemoryPayload( Stream stream, Capture capture, uint version )
	{
		using var reader = new BinaryReader( stream, Encoding.UTF8, leaveOpen: true );
		string[] types = null;
		if ( version >= 2 )
		{
			var typeCount = reader.ReadInt32();
			if ( typeCount is < 0 or > 1_000_000 ) throw new InvalidDataException( "Invalid profiler capture allocation type count." );
			types = new string[typeCount];
			for ( int i = 0; i < typeCount; i++ ) types[i] = ReadText( reader );
		}
		var count = reader.ReadInt32();
		if ( count is < 0 or > 1_000_000 ) throw new InvalidDataException( "Invalid profiler capture allocation count." );
		var allocations = new AllocationSample[count];
		for ( int i = 0; i < count; i++ )
		{
			if ( types is null )
			{
				allocations[i] = new AllocationSample( ReadText( reader ), reader.ReadUInt64(), reader.ReadUInt32(), reader.ReadInt32(), reader.ReadInt64() );
				continue;
			}
			var type = reader.ReadInt32();
			if ( (uint)type >= (uint)types.Length ) throw new InvalidDataException( "Invalid profiler capture allocation type." );
			allocations[i] = new AllocationSample( types[type], reader.ReadUInt64(), reader.ReadUInt32(), reader.ReadInt32(), reader.ReadInt64(), reader.ReadDouble() );
		}

		var frames = capture.Frames;
		var frameCount = reader.ReadInt32();
		if ( frameCount == frames.Length )
		{
			frames = (Frame[])frames.Clone();
			for ( int i = 0; i < frameCount; i++ )
			{
				var bytes = reader.ReadInt64();
				if ( bytes >= 0 ) frames[i] = frames[i] with { AllocatedBytes = bytes };
			}
		}

		// Files saved before the cutoff was stored end here
		var cutoff = frameCount == capture.Frames.Length && stream.Length - stream.Position >= 8 ? reader.ReadDouble() : double.NaN;
		return capture with { Allocations = allocations, Frames = frames, HasGcData = true, AllocationCutoffMs = double.IsFinite( cutoff ) && cutoff >= 0 ? cutoff : null };
	}

	static void WriteCapturePayload( Capture capture, Stream stream )
	{
		if ( !BitConverter.IsLittleEndian || Marshal.SizeOf<Span>() != SpanBytes ) throw new NotSupportedException( "Unsupported profiler capture layout." );
		using var writer = new BinaryWriter( stream, Encoding.UTF8, leaveOpen: true );
		var metadataStart = stream.Position;
		writer.Write( FileMagic ); writer.Write( FileVersion ); writer.Write( SpanBytes );
		writer.Write( capture.DurationMs ); writer.Write( capture.DroppedEvents );
		writer.Write( (capture.EventLimitReached ? 1 : 0) | (capture.LimitReason == "memory limit" ? 2 : 0) );
		writer.Write( capture.Spans.Length ); writer.Write( capture.Sites.Length ); writer.Write( capture.Threads.Length ); writer.Write( capture.InstanceLimit ); WriteText( writer, capture.LimitReason ?? "" );
		foreach ( var site in capture.Sites )
		{
			writer.Write( site.Id ); WriteText( writer, site.Name ); WriteText( writer, site.File ); writer.Write( site.Line );
		}
		foreach ( var thread in capture.Threads )
		{
			writer.Write( thread.Key ); writer.Write( thread.Id ); WriteText( writer, thread.Name );
			var range = capture.ThreadSpans[thread.Key];
			writer.Write( range.Offset ); writer.Write( range.Count );
		}
		writer.Write( capture.Labels.Length );
		for ( int i = 1; i < capture.Labels.Length; i++ ) WriteText( writer, capture.Labels[i] );
		if ( stream.Position - metadataStart > MaxMetadataBytes ) throw new InvalidDataException( "Profiler capture metadata exceeds its limit." );
		writer.Flush();
		stream.Write( MemoryMarshal.AsBytes( capture.Spans.AsSpan() ) );
	}

	static void WriteText( BinaryWriter writer, string text )
	{
		var bytes = Encoding.UTF8.GetBytes( text ?? "" );
		if ( bytes.Length > 65536 ) throw new InvalidDataException( "Profiler capture metadata exceeds its limit." );
		writer.Write( bytes.Length ); writer.Write( bytes );
	}

	static string ReadText( BinaryReader reader )
	{
		var count = reader.ReadInt32();
		if ( count < 0 || count > 65536 || count > reader.BaseStream.Length - reader.BaseStream.Position ) throw new InvalidDataException( "Invalid profiler capture text length." );
		var bytes = reader.ReadBytes( count );
		if ( bytes.Length != count ) throw new EndOfStreamException();
		return StrictUtf8.GetString( bytes );
	}

	static readonly UTF8Encoding StrictUtf8 = new( false, true );
	const long MaxMetadataBytes = 16 * 1024 * 1024;

	internal static Capture ReadCapture( Stream stream, int maximumSpans = MaxEvents / 2, double startMs = 0, double durationMs = 0 )
	{
		try
		{
			var (start, frequency, sections) = ReadDirectory( stream );
			if ( sections.TryGetValue( SnapshotSection, out var snapshot ) )
			{
				if ( snapshot.Version != 1 ) throw new InvalidDataException( "Unsupported profiler snapshot version." );
				if ( startMs != 0 || durationMs != 0 ) throw new ArgumentException( "Time-range loading is supported for streamed recordings." );
				var capture = ReadCaptureData( new SectionStream( stream, snapshot ), maximumSpans );
				if ( sections.TryGetValue( MemorySection, out var memory ) && memory.Version is 1 or 2 )
					capture = ReadMemoryPayload( new SectionStream( stream, memory ), capture, memory.Version );
				return ReadTracks( stream, sections, capture );
			}
			return ReadStreamCapture( stream, start, frequency, sections, maximumSpans, startMs, durationMs );
		}
		catch ( EndOfStreamException e ) { throw new InvalidDataException( "Truncated profiler capture.", e ); }
		catch ( OverflowException e ) { throw new InvalidDataException( "Invalid profiler capture numeric range.", e ); }
		catch ( DecoderFallbackException e ) { throw new InvalidDataException( "Invalid UTF-8 in profiler capture.", e ); }
	}

	static Capture ReadCaptureData( Stream stream, int maximumSpans )
	{
		if ( !BitConverter.IsLittleEndian || Marshal.SizeOf<Span>() != SpanBytes ) throw new NotSupportedException( "Unsupported profiler capture layout." );
		using var reader = new BinaryReader( stream, Encoding.UTF8, leaveOpen: true );
		var magic = reader.ReadUInt64();
		var version = reader.ReadInt32();
		if ( magic != FileMagic || version is not (1 or FileVersion) || reader.ReadInt32() != SpanBytes ) throw new InvalidDataException( "Unrecognized profiler capture format or version." );
		var duration = reader.ReadDouble();
		var dropped = reader.ReadUInt64();
		var flags = reader.ReadInt32();
		var count = reader.ReadInt32();
		var siteCount = reader.ReadInt32();
		var threadCount = reader.ReadInt32();
		var instanceLimit = reader.ReadInt32();
		var reason = ReadText( reader );
		if ( !double.IsFinite( duration ) || duration < 0 || flags is < 0 or > 3 || flags == 2 || count < 0 || count > MaxEvents / 2
			|| siteCount is < 0 or > 32768 || threadCount is < 0 or > 65536 || instanceLimit < 0 || instanceLimit > MaxEvents / 2
			|| (instanceLimit > 0 && count > instanceLimit) || (long)count * SpanBytes > stream.Length - stream.Position ) throw new InvalidDataException( "Invalid profiler capture header." );
		if ( count > maximumSpans ) throw new InvalidDataException( "Capture exceeds available profiler memory. Increase profiler_memory_mb or load a shorter window." );
		var sites = new Site[siteCount];
		var siteIds = new HashSet<uint>();
		for ( int i = 0; i < sites.Length; i++ )
		{
			var site = new Site( reader.ReadUInt32(), ReadText( reader ), ReadText( reader ), reader.ReadInt32() );
			if ( site.Id == 0 || site.Id == uint.MaxValue || !siteIds.Add( site.Id ) ) throw new InvalidDataException( "Invalid profile scope ID." );
			if ( stream.Position > MaxMetadataBytes ) throw new InvalidDataException( "Profiler capture metadata exceeds its limit." );
			sites[i] = site;
		}
		var threads = new ThreadInfo[threadCount];
		var ranges = new Dictionary<ulong, (int Offset, int Count)>();
		int position = 0;
		for ( int i = 0; i < threads.Length; i++ )
		{
			var thread = new ThreadInfo( reader.ReadUInt64(), reader.ReadUInt64(), ReadText( reader ) );
			var offset = reader.ReadInt32(); var length = reader.ReadInt32();
			if ( thread.Key == 0 || offset != position || length < 0 || length > count - position || !ranges.TryAdd( thread.Key, (offset, length) ) ) throw new InvalidDataException( "Invalid profiler thread range." );
			position += length; threads[i] = thread;
			if ( stream.Position > MaxMetadataBytes ) throw new InvalidDataException( "Profiler capture metadata exceeds its limit." );
		}
		string[] labels = [null];
		if ( version >= 2 )
		{
			var labelCount = reader.ReadInt32();
			if ( labelCount is < 1 or > MaxLabels ) throw new InvalidDataException( "Invalid profiler capture label count." );
			labels = new string[labelCount];
			for ( int i = 1; i < labelCount; i++ ) labels[i] = ReadText( reader );
			if ( stream.Position > MaxMetadataBytes ) throw new InvalidDataException( "Profiler capture metadata exceeds its limit." );
		}
		if ( position != count || stream.Length - stream.Position != (long)count * SpanBytes ) throw new InvalidDataException( "Invalid profiler span section length." );
		var spans = GC.AllocateUninitializedArray<Span>( count );
		stream.ReadExactly( MemoryMarshal.AsBytes( spans.AsSpan() ) );
		var threadSpans = new Dictionary<ulong, ArraySegment<Span>>();
		var incomplete = 0;
		foreach ( var (key, range) in ranges )
		{
			var stack = new List<int>();
			double previousStart = 0;
			for ( int i = range.Offset; i < range.Offset + range.Count; i++ )
			{
				ref readonly var s = ref spans[i];
				if ( s.Index != i || s.ThreadKey != key || !siteIds.Contains( s.ScopeId ) || s.Label >= labels.Length
					|| !double.IsFinite( s.StartMs ) || !double.IsFinite( s.EndMs ) || s.StartMs < previousStart || s.EndMs < s.StartMs || s.EndMs > duration
					|| s.Depth > stack.Count || (s.SelfMs is { } self && (!double.IsFinite( self ) || self < 0 || self > s.DurationMs || s.Incomplete)) ) throw new InvalidDataException( "Invalid profile scope instance." );
				for ( int j = s.Depth; j < stack.Count; j++ )
					if ( spans[stack[j]].EndMs > s.StartMs ) throw new InvalidDataException( "Overlapping sibling profile scopes." );
				if ( s.Depth < stack.Count ) stack.RemoveRange( s.Depth, stack.Count - s.Depth );
				var parent = stack.Count == 0 ? -1 : stack[^1];
				if ( s.Parent != parent || (parent >= 0 && s.EndMs > spans[parent].EndMs) ) throw new InvalidDataException( "Invalid profile scope parent." );
				stack.Add( i ); previousStart = s.StartMs;
				if ( s.Incomplete ) incomplete++;
			}
			threadSpans.Add( key, new( spans, range.Offset, range.Count ) );
		}
		return Analyze( new( 0, duration, dropped, (flags & 1) != 0, incomplete, threads, sites, spans, [] )
		{
			ThreadSpans = threadSpans,
			Labels = labels,
			InstanceLimit = instanceLimit,
			LimitReason = flags == 0 ? null : reason.Length > 0 ? reason : (flags & 2) != 0 ? "memory limit" : "event limit"
		} );
	}
}
