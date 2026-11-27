using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Sandbox.Diagnostics;

internal static partial class Profiler
{
	const ulong ContainerMagic = 0x3143454352544253;
	const uint SnapshotSection = 1, MetadataSection = 2, IndexSection = 3, EventsSection = 4, MemorySection = 5, DirectorySection = 0xffff;
	const int ContainerHeaderBytes = 48;
	readonly record struct FileSection( uint Kind, uint Version, long Offset, long Length );
	readonly record struct EventBlock( FileSection Section, ulong Thread, ulong First, ulong Last );

	static void WriteContainerHeader( BinaryWriter writer, ulong start = 0, ulong frequency = 1 )
	{
		writer.Write( ContainerMagic ); writer.Write( 1u ); writer.Write( ContainerHeaderBytes );
		writer.Write( start ); writer.Write( frequency ); writer.Write( 0L ); writer.Write( 0L );
	}

	static FileSection WriteSection( BinaryWriter writer, uint kind, Action write, uint version = 1 )
	{
		writer.Write( kind ); writer.Write( version ); writer.Write( 0L );
		var offset = writer.BaseStream.Position;
		write(); writer.Flush();
		var end = writer.BaseStream.Position;
		writer.BaseStream.Position = offset - 8; writer.Write( end - offset );
		writer.BaseStream.Position = end;
		return new( kind, version, offset, end - offset );
	}

	static void WriteDirectory( BinaryWriter writer, params FileSection[] sections )
	{
		var directory = WriteSection( writer, DirectorySection, () =>
		{
			writer.Write( sections.Length );
			foreach ( var section in sections )
			{
				writer.Write( section.Kind ); writer.Write( section.Version ); writer.Write( section.Offset ); writer.Write( section.Length );
			}
		} );
		var end = writer.BaseStream.Position;
		writer.BaseStream.Position = 32; writer.Write( directory.Offset - 16 ); writer.BaseStream.Position = end;
	}

	static (ulong Start, ulong Frequency, Dictionary<uint, FileSection> Sections) ReadDirectory( Stream stream )
	{
		using var reader = new BinaryReader( stream, Encoding.UTF8, true );
		if ( reader.ReadUInt64() != ContainerMagic || reader.ReadUInt32() != 1 || reader.ReadInt32() != ContainerHeaderBytes ) throw new InvalidDataException( "Unsupported profiler capture container." );
		var start = reader.ReadUInt64(); var frequency = reader.ReadUInt64(); var directory = reader.ReadInt64(); reader.ReadInt64();
		if ( frequency == 0 || directory < ContainerHeaderBytes || directory > stream.Length - 20 ) throw new InvalidDataException( "Invalid profiler capture directory." );
		stream.Position = directory;
		if ( reader.ReadUInt32() != DirectorySection || reader.ReadUInt32() != 1 ) throw new InvalidDataException( "Invalid profiler capture directory type." );
		var length = reader.ReadInt64(); var count = reader.ReadInt32();
		if ( count is < 1 or > 64 || length != 4L + count * 24 || length > stream.Length - directory - 16 ) throw new InvalidDataException( "Invalid profiler capture directory length." );
		var sections = new Dictionary<uint, FileSection>();
		for ( int i = 0; i < count; i++ )
		{
			var section = new FileSection( reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadInt64(), reader.ReadInt64() );
			ValidateSection( stream, section, directory );
			if ( !sections.TryAdd( section.Kind, section ) ) throw new InvalidDataException( "Duplicate profiler capture section." );
		}
		return (start, frequency, sections);
	}

	static void ValidateSection( Stream stream, FileSection section, long end, bool restorePosition = true )
	{
		if ( section.Offset < ContainerHeaderBytes + 16 || section.Offset > end || section.Length < 0 || section.Length > end - section.Offset ) throw new InvalidDataException( "Profiler capture section is outside the file." );
		var position = stream.Position;
		stream.Position = section.Offset - 16;
		using var reader = new BinaryReader( stream, Encoding.UTF8, true );
		var valid = reader.ReadUInt32() == section.Kind && reader.ReadUInt32() == section.Version && reader.ReadInt64() == section.Length;
		if ( restorePosition ) stream.Position = position;
		if ( !valid ) throw new InvalidDataException( "Profiler capture section header mismatch." );
	}

	sealed class SectionStream( Stream source, FileSection section ) : Stream
	{
		long _position;
		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => section.Length;
		public override long Position { get => _position; set => Seek( value, SeekOrigin.Begin ); }
		public override int Read( byte[] buffer, int offset, int count ) => Read( buffer.AsSpan( offset, count ) );
		public override int Read( System.Span<byte> buffer )
		{
			if ( source.Position != section.Offset + _position ) source.Position = section.Offset + _position;
			var count = source.Read( buffer[..(int)Math.Min( buffer.Length, Length - _position )] );
			_position += count; return count;
		}
		public override long Seek( long offset, SeekOrigin origin )
		{
			var position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _position + offset, SeekOrigin.End => Length + offset, _ => -1 };
			if ( position < 0 || position > Length ) throw new InvalidDataException( "Seek outside profiler capture section." );
			return _position = position;
		}
		public override void Flush() { }
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
	}

	sealed class ArchiveWriter
	{
		const long DiskLimit = 8L * 1024 * 1024 * 1024;
		readonly record struct Batch( NativeThread Thread, NativeEvent[] Events, int Count );
		readonly BlockingCollection<Batch> _queue = new();
		long _queuedEvents;
		const int MaxQueuedEvents = 2_097_152;
		ulong _stopTick, _start;
		readonly ulong _frequency;
		public ulong StopTick => System.Threading.Volatile.Read( ref _stopTick );
		readonly List<EventBlock> _blocks = new();
		// Each thread's events gather into blocks of this many, rather than one block per collector pass
		const int BlockEvents = 4096;
		readonly Dictionary<ulong, Batch> _pending = new();
		readonly Dictionary<ulong, List<NativeEvent>> _stacks = new();
		readonly Dictionary<ulong, ulong> _lastTicks = new();
		readonly FileStream _stream;
		readonly BinaryWriter _writer;
		readonly System.Threading.Thread _thread;
		readonly string _path, _temporary;
		volatile string _stopReason;
		Exception _error;
		public string StopReason => _stopReason;
		public string Name => Path.GetFileName( _path );

		public ArchiveWriter( ulong frequency )
		{
			_frequency = frequency;
			Directory.CreateDirectory( CaptureDirectory );
			foreach ( var partial in Directory.EnumerateFiles( CaptureDirectory, "capture-*.sprof.partial" ) )
			{
				var owner = Path.GetFileName( partial ).Split( '-' )[1];
				if ( !int.TryParse( owner, out var pid ) ) continue;
				try { using var process = System.Diagnostics.Process.GetProcessById( pid ); if ( !process.HasExited ) continue; }
				catch ( ArgumentException ) { }
				catch ( System.ComponentModel.Win32Exception ) { continue; }
				try { File.Delete( partial ); } catch ( IOException ) { } catch ( UnauthorizedAccessException ) { }
			}
			_path = CapturePath( $"capture-{Environment.ProcessId}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}" );
			_temporary = _path + ".partial";
			_stream = new FileStream( _temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 64 * 1024 );
			_writer = new BinaryWriter( _stream, Encoding.UTF8, true );
			_thread = new System.Threading.Thread( WriteLoop ) { IsBackground = true, Name = "Profile writer" };
			try { WriteContainerHeader( _writer, 0, frequency ); _thread.Start(); }
			catch
			{
				try { _stream.Dispose(); } catch { }
				try { File.Delete( _temporary ); } catch { }
				_queue.Dispose(); throw;
			}
		}

		public bool Enqueue( NativeThread thread, NativeEvent[] events, int count )
		{
			if ( StopReason is not null ) { RequestStop( StopReason, events[0].Ticks ); return false; }
			var copy = ArrayPool<NativeEvent>.Shared.Rent( count );
			if ( System.Threading.Interlocked.Add( ref _queuedEvents, copy.Length ) <= MaxQueuedEvents )
			{
				events.AsSpan( 0, count ).CopyTo( copy );
				if ( _queue.TryAdd( new( thread, copy, count ) ) ) return true;
			}
			System.Threading.Interlocked.Add( ref _queuedEvents, -copy.Length );
			ArrayPool<NativeEvent>.Shared.Return( copy );
			RequestStop( "disk writer backlog", events[0].Ticks );
			return false;
		}

		void RequestStop( string reason, ulong tick )
		{
			ulong previous;
			do { previous = StopTick; if ( previous != 0 && previous <= tick ) break; }
			while ( System.Threading.Interlocked.CompareExchange( ref _stopTick, tick, previous ) != previous );
			System.Threading.Interlocked.CompareExchange( ref _stopReason, reason, null );
		}

		void WriteLoop()
		{
			try
			{
				foreach ( var batch in _queue.GetConsumingEnumerable() )
				{
					try
					{
						if ( _error is not null ) { RequestStop( "disk write failed", batch.Events[0].Ticks ); continue; }
						if ( _stream.Position >= DiskLimit || _blocks.Count >= 999_000 )
						{
							RequestStop( _blocks.Count >= 999_000 ? "disk index limit" : "disk size limit", batch.Events[0].Ticks );
							continue;
						}
						Append( batch );
					}
					catch ( Exception e ) { _error = e; RequestStop( "disk write failed", batch.Events[0].Ticks ); }
					finally { System.Threading.Interlocked.Add( ref _queuedEvents, -batch.Events.Length ); ArrayPool<NativeEvent>.Shared.Return( batch.Events ); }
				}
			}
			catch ( Exception e ) { _error = e; System.Threading.Interlocked.CompareExchange( ref _stopReason, "disk write failed", null ); }
		}

		void Append( Batch batch )
		{
			var key = batch.Thread.Key;
			if ( !_pending.TryGetValue( key, out var pending ) ) pending = new( batch.Thread, new NativeEvent[BlockEvents], 0 );
			var events = batch.Events.AsSpan( 0, batch.Count );
			while ( events.Length > 0 )
			{
				var take = Math.Min( events.Length, BlockEvents - pending.Count );
				events[..take].CopyTo( pending.Events.AsSpan( pending.Count ) );
				events = events[take..];
				pending = pending with { Count = pending.Count + take };
				if ( pending.Count < BlockEvents ) continue;
				WriteBatch( pending );
				pending = pending with { Count = 0 };
			}
			_pending[key] = pending;
		}

		void WriteBatch( Batch batch )
		{
			if ( !_stacks.TryGetValue( batch.Thread.Key, out var stack ) ) _stacks.Add( batch.Thread.Key, stack = new() );
			var first = batch.Events[0].Ticks;
			var last = batch.Events[batch.Count - 1].Ticks;
			var stride = last - first <= uint.MaxValue ? 16 : 20;
			var eventOffset = 44 + stack.Count * 20;
			var byteCount = eventOffset + batch.Count * stride;
			var data = ArrayPool<byte>.Shared.Rent( byteCount );
			try
			{
				BinaryPrimitives.WriteUInt32LittleEndian( data, EventsSection );
				BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 4 ), 1 );
				BinaryPrimitives.WriteInt64LittleEndian( data.AsSpan( 8 ), byteCount - 16 );
				BinaryPrimitives.WriteUInt64LittleEndian( data.AsSpan( 16 ), batch.Thread.Key );
				BinaryPrimitives.WriteUInt64LittleEndian( data.AsSpan( 24 ), first );
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 32 ), stride );
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 36 ), stack.Count );
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 40 ), batch.Count );
				for ( int i = 0; i < stack.Count; i++ )
				{
					var bytes = data.AsSpan( 44 + i * 20, 20 ); var open = stack[i];
					BinaryPrimitives.WriteUInt64LittleEndian( bytes, open.Ticks );
					BinaryPrimitives.WriteUInt64LittleEndian( bytes[8..], open.Token );
					BinaryPrimitives.WriteUInt32LittleEndian( bytes[16..], open.Scope );
				}
				for ( int i = 0; i < batch.Count; i++ )
				{
					var e = batch.Events[i]; var bytes = data.AsSpan( eventOffset + i * stride, stride );
					BinaryPrimitives.WriteUInt64LittleEndian( bytes, e.Token );
					BinaryPrimitives.WriteUInt32LittleEndian( bytes[8..], e.Scope );
					if ( stride == 16 ) BinaryPrimitives.WriteUInt32LittleEndian( bytes[12..], (uint)(e.Ticks - first) );
					else BinaryPrimitives.WriteUInt64LittleEndian( bytes[12..], e.Ticks - first );
				}
				var section = new FileSection( EventsSection, 1, _stream.Position + 16, byteCount - 16 );
				_stream.Write( data.AsSpan( 0, byteCount ) );
				_blocks.Add( new( section, batch.Thread.Key, first, last ) );
				_lastTicks[batch.Thread.Key] = last;
				for ( int i = 0; i < batch.Count; i++ ) ApplyStackEvent( stack, batch.Events[i] );
			}
			finally { ArrayPool<byte>.Shared.Return( data ); }
		}

		public void Abort()
		{
			try { _queue.CompleteAdding(); _thread.Join(); }
			catch { }
			finally
			{
				try { _stream.Dispose(); } catch { }
				try { _queue.Dispose(); File.Delete( _temporary ); } catch { }
			}
		}

		public void SetStart( ulong start )
		{
			_start = start;
			_stream.Position = 16; _writer.Write( start ); _stream.Position = ContainerHeaderBytes;
		}

		public string Finish( Capture metadata, ulong end )
		{
			_queue.CompleteAdding(); _thread.Join();
			try
			{
				if ( _error is not null ) throw new IOException( "Profiler capture could not be written.", _error );
				foreach ( var pending in _pending.Values )
					if ( pending.Count > 0 ) WriteBatch( pending );
				if ( StopTick != 0 ) end = Math.Max( _start, Math.Min( end, StopTick ) );
				foreach ( var (key, stack) in _stacks )
				{
					if ( stack.Count == 0 ) continue;
					var checkpointTime = Math.Max( end, _lastTicks.GetValueOrDefault( key ) );
					var checkpoint = WriteSection( _writer, EventsSection, () =>
					{
						_writer.Write( key ); _writer.Write( checkpointTime ); _writer.Write( 16 ); _writer.Write( stack.Count ); _writer.Write( 0 );
						foreach ( var open in stack ) { _writer.Write( open.Ticks ); _writer.Write( open.Token ); _writer.Write( open.Scope ); }
					} );
					_blocks.Add( new( checkpoint, key, checkpointTime, checkpointTime ) );
				}
				metadata = metadata with { DurationMs = (end - _start) * (1000.0 / _frequency), EventLimitReached = StopReason is not null, LimitReason = StopReason };
				var info = WriteSection( _writer, MetadataSection, () => WriteCapturePayload( metadata, _stream ) );
				var index = WriteSection( _writer, IndexSection, () =>
				{
					_writer.Write( _blocks.Count );
					foreach ( var block in _blocks )
					{
						_writer.Write( block.Section.Offset ); _writer.Write( block.Section.Length );
						_writer.Write( block.Thread ); _writer.Write( block.First ); _writer.Write( block.Last );
					}
				} );
				WriteDirectory( _writer, info, index );
				_writer.Flush(); _stream.Dispose();
				File.Move( _temporary, _path, false );
				return Name;
			}
			finally
			{
				try { _stream.Dispose(); }
				finally { _queue.Dispose(); if ( File.Exists( _temporary ) ) File.Delete( _temporary ); }
			}
		}
	}

	static void ApplyStackEvent( List<NativeEvent> stack, NativeEvent e )
	{
		if ( e.Scope == uint.MaxValue ) { stack.Clear(); return; }
		if ( e.Scope != 0 )
		{
			if ( stack.Count >= ushort.MaxValue ) throw new InvalidDataException( "Profile scope nesting exceeds the capture limit." );
			stack.Add( e ); return;
		}
		for ( int i = stack.Count - 1; i >= 0; i-- )
			if ( stack[i].Token == e.Token ) { stack.RemoveRange( i, stack.Count - i ); return; }
	}

	static bool ValidBegin( uint scope, HashSet<uint> sites, int labelCount ) => sites.Contains( scope & ScopeMask ) && scope >> LabelShift < labelCount;

	static Capture ReadStreamCapture( Stream stream, ulong start, ulong frequency, Dictionary<uint, FileSection> sections, int maximumSpans, double startMs, double durationMs )
	{
		if ( !sections.TryGetValue( MetadataSection, out var info ) || !sections.TryGetValue( IndexSection, out var index ) || info.Version != 1 || index.Version != 1 ) throw new InvalidDataException( "Missing or unsupported profiler stream index." );
		var metadata = ReadCaptureData( new SectionStream( stream, info ), MaxEvents / 2 );
		if ( !double.IsFinite( startMs ) || !double.IsFinite( durationMs ) || startMs < 0 || (startMs > 0 && startMs >= metadata.DurationMs) || durationMs < 0 ) throw new ArgumentOutOfRangeException( nameof( startMs ), "Choose a time range within the recording." );
		var endMs = durationMs == 0 ? metadata.DurationMs : Math.Min( metadata.DurationMs, startMs + durationMs );
		var windowStart = checked(start + (ulong)(startMs * frequency / 1000));
		var windowEnd = checked(start + (ulong)(endMs * frequency / 1000));
		var builder = new Builder { Session = 1, Start = windowStart, Deadline = windowEnd, EventLimit = maximumSpans * 2, PendingBudgetBytes = maximumSpans * 112L, MemoryBounded = true };
		var sites = metadata.Sites.Select( x => x.Id ).ToHashSet();
		var threads = metadata.Threads.ToDictionary( x => x.Key );
		if ( index.Length < 4 || index.Length > 40_000_004 ) throw new InvalidDataException( "Invalid profiler stream index size." );
		var indexData = ArrayPool<byte>.Shared.Rent( (int)index.Length );
		var started = new HashSet<ulong>();
		var lastTimes = new Dictionary<ulong, ulong>();
		try
		{
			using ( var section = new SectionStream( stream, index ) ) section.ReadExactly( indexData.AsSpan( 0, (int)index.Length ) );
			using var reader = new BinaryReader( new MemoryStream( indexData, 0, (int)index.Length, writable: false ), Encoding.UTF8 );
			var count = reader.ReadInt32();
			if ( count < 0 || count > 1_000_000 || index.Length != 4L + count * 40 ) throw new InvalidDataException( "Invalid profiler stream index size." );
			for ( int i = 0; i < count && !builder.LimitReached; i++ )
			{
				var block = new EventBlock( new( EventsSection, 1, reader.ReadInt64(), reader.ReadInt64() ), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64() );
				if ( !threads.ContainsKey( block.Thread ) || block.First < start || block.Last < block.First || (lastTimes.TryGetValue( block.Thread, out var last ) && block.First < last) ) throw new InvalidDataException( "Invalid profiler stream time index." );
				lastTimes[block.Thread] = block.Last;
				if ( block.Last < windowStart || (block.First > windowEnd && started.Contains( block.Thread )) ) continue;
				ValidateSection( stream, block.Section, Math.Min( info.Offset - 16, index.Offset - 16 ), restorePosition: false );
				ReadEventBlock( stream, block, builder, windowEnd, threads[block.Thread], sites, metadata.Labels.Length, started.Add( block.Thread ) );
			}
			// A window cut short by the span limit ends where it was cut
			var captureEnd = builder.RejectedTick != 0 ? Math.Clamp( builder.RejectedTick, windowStart, windowEnd ) : windowEnd;
			var capture = Finish( builder, captureEnd, metadata.DroppedEvents, 1000.0 / frequency, metadata.Sites, metadata.Threads );
			return capture with
			{
				SourceOffsetMs = startMs,
				RecordedDurationMs = metadata.DurationMs,
				Labels = metadata.Labels,
				EventLimitReached = capture.EventLimitReached || metadata.EventLimitReached,
				LimitReason = capture.LimitReason ?? metadata.LimitReason
			};
		}
		finally { ArrayPool<byte>.Shared.Return( indexData ); foreach ( var state in builder.Threads.Values ) state.Spans.Dispose(); builder.StopCollector.Dispose(); }
	}

	static void ReadEventBlock( Stream stream, EventBlock block, Builder builder, ulong end, ThreadInfo threadInfo, HashSet<uint> sites, int labelCount, bool firstBlock )
	{
		using var section = new SectionStream( stream, block.Section );
		using var reader = new BinaryReader( section, Encoding.UTF8, true );
		var key = reader.ReadUInt64(); var first = reader.ReadUInt64(); var stride = reader.ReadInt32(); var openCount = reader.ReadInt32(); var count = reader.ReadInt32();
		if ( key != block.Thread || first != block.First || (stride != 16 && stride != 20) || openCount < 0 || openCount > ushort.MaxValue || count < 0 || count > RingCapacity || section.Length != 28L + openCount * 20L + count * (long)stride ) throw new InvalidDataException( "Invalid profiler event block." );
		var opens = new List<NativeEvent>();
		for ( int i = 0; i < openCount; i++ )
		{
			var e = new NativeEvent { Ticks = reader.ReadUInt64(), Token = reader.ReadUInt64(), Scope = reader.ReadUInt32(), Session = 1 };
			if ( e.Ticks > first || !ValidBegin( e.Scope, sites, labelCount ) || e.Token == 0 ) throw new InvalidDataException( "Invalid profiler stack checkpoint." );
			if ( firstBlock ) opens.Add( e );
		}
		var data = ArrayPool<byte>.Shared.Rent( count * stride );
		try
		{
			section.ReadExactly( data.AsSpan( 0, count * stride ) );
			var thread = new NativeThread { Key = key, Id = threadInfo.Id };
			ThreadState state = null;
			bool initialized = !firstBlock;
			void InitializeStack()
			{
				if ( initialized ) return;
				foreach ( var open in opens )
				{
					if ( open.Ticks > end ) break;
					var clipped = open; clipped.Ticks = Math.Max( builder.Start, open.Ticks );
					builder.Accept( thread, clipped, ref state );
					if ( builder.LimitReached ) break;
					state.Spans[state.Spans.Count - 1].Incomplete = open.Ticks < builder.Start;
				}
				initialized = true;
			}
			if ( count == 0 ) InitializeStack();
			ulong previous = first;
			for ( int i = 0; i < count && !builder.LimitReached; i++ )
			{
				var bytes = data.AsSpan( i * stride, stride );
				var delta = stride == 16 ? BinaryPrimitives.ReadUInt32LittleEndian( bytes[12..] ) : BinaryPrimitives.ReadUInt64LittleEndian( bytes[12..] );
				if ( delta > ulong.MaxValue - first ) throw new InvalidDataException( "Invalid profiler timestamp delta." );
				var e = new NativeEvent { Token = BinaryPrimitives.ReadUInt64LittleEndian( bytes ), Scope = BinaryPrimitives.ReadUInt32LittleEndian( bytes[8..] ), Ticks = first + delta, Session = 1 };
				if ( e.Ticks < previous || e.Ticks > block.Last || (e.Scope != 0 && e.Scope != uint.MaxValue && !ValidBegin( e.Scope, sites, labelCount )) || (e.Scope != uint.MaxValue && e.Token == 0) ) throw new InvalidDataException( "Invalid profiler event." );
				previous = e.Ticks;
				if ( e.Ticks < builder.Start ) { if ( firstBlock ) ApplyStackEvent( opens, e ); continue; }
				InitializeStack();
				if ( e.Ticks > end ) break;
				builder.Accept( thread, e, ref state );
			}
		}
		finally { ArrayPool<byte>.Shared.Return( data ); }
	}
}
