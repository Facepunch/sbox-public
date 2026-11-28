using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox.ContentBuild;

/// <summary>
/// The last successful build of one source asset: everything needed to tell whether its outputs are
/// still current, and which outputs it owns. Written only after those outputs are in place.
/// </summary>
public sealed class AssetRecord
{
	public string Source { get; set; }
	public string Job { get; set; }

	/// <summary>
	/// Published output paths and their last known contents.
	/// </summary>
	[JsonRequired, JsonObjectCreationHandling( JsonObjectCreationHandling.Populate )]
	public Dictionary<string, FileStamp> Outputs { get; set; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Source files checked for changes before reusing these outputs.
	/// </summary>
	[JsonRequired]
	public List<InputStamp> Inputs { get; set; } = [];

	/// <summary>
	/// Compiler dependencies checked by fingerprint.
	/// </summary>
	[JsonRequired]
	public List<SpecialDependency> Special { get; set; } = [];

	/// <summary>
	/// Compiler arguments used by this asset.
	/// </summary>
	[JsonRequired]
	public Dictionary<string, string> Arguments { get; set; } = [];

	/// <summary>
	/// Compiled files read from other assets.
	/// </summary>
	[JsonRequired]
	public List<RecordedPrerequisite> Prerequisites { get; set; } = [];

	/// <summary>
	/// An input was edited while this compiled, so the output may be older than the inputs recorded.
	/// </summary>
	public bool ChangedWhileCompiling { get; set; }

	/// <summary>
	/// How long the compile took, so the longest start first when it compiles again.
	/// </summary>
	public double Seconds { get; set; }

	/// <summary>
	/// A child source's bytes and fingerprint, as it was last built from them (<see cref="ChildSource"/>): what it
	/// builds from again when no compile declares it this build. Null for a source file.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public byte[] Input { get; set; }

	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public uint Fingerprint { get; set; }
}

public sealed record FileStamp( long Size, long Time, string Hash );

/// <param name="Size">With <paramref name="Time"/>, the file as last seen matching <paramref name="Checksum"/>.</param>
public sealed record InputStamp( string Path, bool Exists, uint Checksum, long Size, long Time );

/// <param name="File">Absolute path of the compiled file read, which names its producer in any root.</param>
/// <param name="Hash">Its hash when it was read, "0" if it didn't exist.</param>
/// <param name="Output">The producer's output path within its root, which a later build may resolve to a
/// different root; empty for a file no root of the build produced. Null in records written before it existed.</param>
/// <param name="OrderOnly">Only whether it exists matters, not its contents.</param>
public sealed record RecordedPrerequisite( string File, string Hash, string Output = null, bool OrderOnly = false );

/// <summary>
/// A publication failed part way (a file in use, read-only, ...) and was put back as it was.
/// </summary>
public sealed class PublishFailedException( string message, Exception inner ) : Exception( message, inner );

/// <summary>
/// One record file per source under &lt;state&gt;/records. Publishing a job is a small transaction: a journal
/// lists every file it will replace or delete, replaced files go to a backup, then the record is written.
/// Writing the record is the commit. On startup, journals without their record are rolled back.
/// </summary>
sealed class RecordStore
{
	readonly Func<string, string> resolve;
	readonly string records, journals, backups, deferredFile, indexFile;
	Dictionary<string, string> deferred;
	readonly HashSet<string> quarantined;
	readonly HashSet<string> unknownOwners = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, (string Name, long Size, long Time, bool Written)> recordStamps = new( StringComparer.OrdinalIgnoreCase );
	readonly long loadTimeUtcTicks;
	bool indexDirty;

	const uint IndexMagic = 0x52454353; // "SCER"
	const int IndexVersion = 1;
	static readonly long RacyWindowTicks = TimeSpan.FromSeconds( 2 ).Ticks;

	sealed record IndexEntry( long Size, long Time, AssetRecord Record );
	sealed record RecordIndex( long SnapshotTimeUtcTicks, Dictionary<string, IndexEntry> Entries );

	/// <summary>
	/// Whether any live source still has unknown outputs.
	/// </summary>
	public bool HasUnknownOwners => unknownOwners.Count > 0;

	static readonly JsonSerializerOptions Json = new() { WriteIndented = true, RespectRequiredConstructorParameters = true };

	public Dictionary<string, AssetRecord> All { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <param name="resolve">Where an output key is on disk, or null if its root isn't mounted in this build.</param>
	public RecordStore( Func<string, string> resolve, string state )
	{
		this.resolve = resolve;
		records = Directory.CreateDirectory( Path.Combine( state, "records" ) ).FullName;
		journals = Directory.CreateDirectory( Path.Combine( state, "journal" ) ).FullName;
		backups = Path.Combine( state, "backup" );
		deferredFile = Path.Combine( state, "deferred-deletes.json" );
		indexFile = Path.Combine( state, "records.bin" );
		if ( File.Exists( deferredFile ) ) deferred = ReadCache( deferredFile, ReadDeferred );

		// A journal still being written was never renamed, so nothing it lists was moved
		foreach ( var partial in Directory.GetFiles( journals, "*.tmp" ) ) File.Delete( partial );
		foreach ( var journal in Directory.GetFiles( journals, "*.json" ) ) Recover( journal );

		// Captured before reading any record so we never promote a file that was already racy at load time.
		loadTimeUtcTicks = DateTime.UtcNow.Ticks;

		var files = new DirectoryInfo( records ).GetFiles( "*.json" );
		var index = TryReadIndex();
		var racyCutoff = index is null ? long.MinValue : index.SnapshotTimeUtcTicks - RacyWindowTicks;
		var misses = new List<FileInfo>();

		foreach ( var file in files )
		{
			if ( index is not null
				&& index.Entries.TryGetValue( file.Name, out var entry )
				&& entry.Size == file.Length
				&& entry.Time == file.LastWriteTimeUtc.Ticks
				&& file.LastWriteTimeUtc.Ticks < racyCutoff )
			{
				All[entry.Record.Source] = entry.Record;
				recordStamps[entry.Record.Source] = (file.Name, entry.Size, entry.Time, false);
			}
			else
			{
				misses.Add( file );
			}
		}

		if ( misses.Count > 0 )
		{
			var loaded = new (AssetRecord Record, (string Name, long Size, long Time, bool Written)? Stamp)[misses.Count];
			Func<string, AssetRecord> read = ReadRecord;
			Parallel.For( 0, misses.Count, i =>
			{
				var file = misses[i];
				var sizeBefore = file.Length;
				var timeBefore = file.LastWriteTimeUtc.Ticks;
				var record = ReadCache( file.FullName, read );
				if ( record is null ) return;

				// Only cache metadata if the file did not change while we read it
				var after = new FileInfo( file.FullName );
				var stamp = after.Exists && after.Length == sizeBefore && after.LastWriteTimeUtc.Ticks == timeBefore
					? (file.Name, sizeBefore, timeBefore, false)
					: ((string, long, long, bool)?)null;
				loaded[i] = (record, stamp);
			} );
			foreach ( var (record, stamp) in loaded )
			{
				if ( record is null ) continue;
				All[record.Source] = record;
				if ( stamp is { } verified ) recordStamps[record.Source] = verified;
			}
		}

		indexDirty = index is null || misses.Count > 0 || index.Entries.Count != recordStamps.Count;

		// Keep the damaged bytes for inspection even after the source has a new record.
		quarantined = Directory.EnumerateFiles( records, "*.json.invalid" )
			.Select( file => Path.ChangeExtension( file, null ) )
			.Where( file => !File.Exists( file ) )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );
	}

	/// <summary>
	/// Keep malformed cache data for inspection. Only journal recovery needs a readable commit marker.
	/// </summary>
	static T ReadCache<T>( string file, Func<string, T> read ) where T : class
	{
		try
		{
			return read( file );
		}
		catch ( JsonException )
		{
			File.Move( file, file + ".invalid", true );
			return null;
		}
	}

	static Dictionary<string, string> ReadDeferred( string file )
	{
		var entries = JsonSerializer.Deserialize<Dictionary<string, string>>( File.ReadAllText( file ) );
		var paths = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (key, hash) in entries ?? throw new JsonException( $"Missing deferred deletions: {file}" ) )
		{
			if ( string.IsNullOrWhiteSpace( key ) || key.Contains( '\0' ) || string.IsNullOrEmpty( hash ) || !paths.TryAdd( key, hash ) )
				throw new JsonException( $"Invalid deferred deletion in {file}" );
		}
		return paths;
	}

	AssetRecord ReadRecord( string file )
	{
		var record = JsonSerializer.Deserialize<AssetRecord>( File.ReadAllText( file ), Json );

		if ( string.IsNullOrWhiteSpace( record?.Source ) || !Guid.TryParseExact( record.Job, "N", out _ )
			|| !RecordPath( record.Source ).Equals( file, StringComparison.OrdinalIgnoreCase ) )
			throw new JsonException( $"Invalid source or job in content build record: {file}" );

		if ( record.Outputs is not { Count: > 0 } || record.Inputs is null || record.Special is null
			|| record.Arguments is null || record.Prerequisites is null )
			throw new JsonException( $"Missing dependencies or outputs in content build record: {file}" );

		if ( record.Outputs.Any( x => string.IsNullOrWhiteSpace( x.Key ) || x.Key.Contains( '\0' ) || string.IsNullOrEmpty( x.Value?.Hash ) )
			|| record.Inputs.Any( x => !ValidPath( x?.Path ) )
			|| record.Special.Any( x => x?.Compiler is null || x.Name is null )
			|| record.Prerequisites.Any( x => !ValidPath( x?.File ) || string.IsNullOrEmpty( x.Hash ) || x.Output?.Contains( '\0' ) == true ) )
			throw new JsonException( $"Invalid dependency or output in content build record: {file}" );

		return record;

		static bool ValidPath( string path ) => path is not null && Path.IsPathFullyQualified( path ) && !path.Contains( '\0' );
	}

	/// <summary>
	/// Match quarantined filenames to live sources during inventory. Deleted sources no longer own files.
	/// </summary>
	public void CheckSource( string source, string output )
	{
		if ( All.TryGetValue( source, out var record ) )
		{
			if ( output is null || record.Outputs.ContainsKey( output ) ) return;

			var file = RecordPath( source );
			File.Move( file, file + ".invalid", true );
			All.Remove( source );
			recordStamps.Remove( source );
			unknownOwners.Add( source );
			indexDirty = true;
		}
		else if ( quarantined.Count > 0 && quarantined.Contains( RecordPath( source ) ) )
		{
			unknownOwners.Add( source );
		}
	}

	public IEnumerable<string> Owners( string output ) => All.Values.Where( x => x.Outputs.ContainsKey( output ) ).Select( x => x.Source );

	string RecordPath( string source ) => Path.Combine( records, $"{XxHash3.HashToUInt64( System.Text.Encoding.UTF8.GetBytes( source.ToLowerInvariant() ) ):x16}.json" );

	sealed record Move( string Target, string Staged, bool Existed );
	sealed record Journal( string Job, string Source, bool Remove, List<Move> Moves );
	sealed record CommitMarker( string Source, string Job );

	/// <summary>
	/// Puts staged outputs in place, deletes <paramref name="obsolete"/> outputs and commits
	/// <paramref name="record"/>, or removes the source's record when it is null. Unchanged files are left alone.
	/// A file that can't be moved undoes the publication at once (<see cref="PublishFailedException"/>); one that
	/// can't even be put back is thrown as is and left to the next build's recovery.
	/// </summary>
	/// <returns>Absolute paths of the files replaced, added or deleted.</returns>
	public List<string> Publish( string source, string job, AssetRecord record, IReadOnlyDictionary<string, string> staged, IEnumerable<string> obsolete, Action<string> beforeCommit = null )
	{
		var moves = new List<Move>();
		foreach ( var (rel, file) in staged )
		{
			var target = resolve( rel );
			var existed = File.Exists( target );
			if ( existed && Hashing.File( target ) == record.Outputs[rel].Hash ) continue;
			moves.Add( new Move( target, file, existed ) );
		}

		var deferredChanged = false;
		foreach ( var rel in obsolete )
		{
			var target = resolve( rel );
			if ( target is null || !File.Exists( target ) ) continue;

			if ( HasUnknownOwners )
			{
				deferred ??= new( StringComparer.OrdinalIgnoreCase );
				deferred[rel] = All[source].Outputs[rel].Hash;
				deferredChanged = true;
				continue;
			}

			moves.Add( new Move( target, null, true ) );
		}

		// Persist candidates before dropping ownership. If publication rolls back, the old record still protects them.
		if ( deferredChanged ) WriteJson( deferredFile, deferred );

		// Complete before anything moves, so recovery only ever sees a whole journal
		var journal = Path.Combine( journals, $"{job}.json" );
		File.WriteAllText( journal + ".tmp", JsonSerializer.Serialize( new Journal( job, source, record is null, moves ) ) );
		File.Move( journal + ".tmp", journal );

		var backup = Path.Combine( backups, job );
		try
		{
			foreach ( var move in moves )
			{
				if ( move.Existed ) MoveFile( move.Target, BackupPath( backup, move.Target ) );
				if ( move.Staged is not null ) MoveFile( move.Staged, move.Target );
			}
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			throw Undo( journal, e );
		}

		beforeCommit?.Invoke( source );

		try
		{
			// Unchanged files kept their old times
			foreach ( var rel in staged.Keys ) record.Outputs[rel] = Hashing.Stamp( resolve( rel ) );

			if ( record is null ) File.Delete( RecordPath( source ) );
			else Rewrite( source, record );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			throw Undo( journal, e );
		}

		if ( record is null )
		{
			All.Remove( source );
			recordStamps.Remove( source );
		}
		else
		{
			All[source] = record;
		}
		unknownOwners.Remove( source );
		indexDirty = true;

		File.Delete( journal );
		if ( Directory.Exists( backup ) ) Directory.Delete( backup, true );
		return moves.Select( x => x.Target ).ToList();
	}

	/// <summary>
	/// Writes a record again with nothing to move, as when its outputs' stamps moved but their bytes didn't.
	/// </summary>
	public void Rewrite( string source, AssetRecord record )
	{
		var path = RecordPath( source );
		WriteJson( path, record );
		var info = new FileInfo( path );
		if ( info.Exists ) recordStamps[source] = (info.Name, info.Length, info.LastWriteTimeUtc.Ticks, true);
		else recordStamps.Remove( source );
		indexDirty = true;
	}

	/// <summary>
	/// Writes a single-file binary snapshot of the current records so subsequent builds avoid opening
	/// and JSON-parsing thousands of individual record files. Best-effort: <c>records/*.json</c> remains
	/// the crash-safe source of truth.
	/// </summary>
	public void SaveIndex()
	{
		if ( !indexDirty || !Directory.Exists( records ) ) return;

		try
		{
			var saveTime = DateTime.UtcNow.Ticks;
			var loadRacyCutoff = loadTimeUtcTicks - RacyWindowTicks;

			// Verify current directory metadata without opening files: omit any entry whose JSON changed
			// externally mid-build, and never promote a file that was already racy when loaded.
			var onDisk = new Dictionary<string, (long Size, long Time)>( StringComparer.OrdinalIgnoreCase );
			foreach ( var file in new DirectoryInfo( records ).EnumerateFiles( "*.json" ) )
				onDisk[file.Name] = (file.Length, file.LastWriteTimeUtc.Ticks);

			var entries = new List<(string Name, long Size, long Time, AssetRecord Record)>( All.Count );
			foreach ( var record in All.Values )
			{
				if ( !recordStamps.TryGetValue( record.Source, out var stamp ) ) continue;
				if ( !stamp.Written && stamp.Time >= loadRacyCutoff ) continue;
				if ( !onDisk.TryGetValue( stamp.Name, out var disk ) || disk.Size != stamp.Size || disk.Time != stamp.Time ) continue;
				entries.Add( (stamp.Name, stamp.Size, stamp.Time, record) );
			}

			using var stream = new MemoryStream( Math.Max( 256, entries.Count * 512 ) );
			using ( var writer = new BinaryWriter( stream, Encoding.UTF8, leaveOpen: true ) )
			{
				writer.Write( IndexMagic );
				writer.Write( IndexVersion );
				writer.Write( saveTime );
				writer.Write( entries.Count );
				foreach ( var (name, size, time, record) in entries )
				{
					writer.Write( name );
					writer.Write( size );
					writer.Write( time );
					WriteRecord( writer, record );
				}
			}

			var payloadLength = (int)stream.Length;
			var hash = XxHash3.HashToUInt64( stream.GetBuffer().AsSpan( 0, payloadLength ) );
			Span<byte> trailer = stackalloc byte[8];
			BinaryPrimitives.WriteUInt64LittleEndian( trailer, hash );
			stream.Write( trailer );

			var tmp = indexFile + ".tmp";
			using ( var fs = File.Create( tmp ) )
				fs.Write( stream.GetBuffer(), 0, (int)stream.Length );
			File.Move( tmp, indexFile, true );
			indexDirty = false;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException ) { }
	}

	RecordIndex TryReadIndex()
	{
		if ( !File.Exists( indexFile ) ) return null;

		try
		{
			var bytes = File.ReadAllBytes( indexFile );
			if ( bytes.Length < 28 ) return null;

			var payloadLength = bytes.Length - 8;
			var expected = BinaryPrimitives.ReadUInt64LittleEndian( bytes.AsSpan( payloadLength, 8 ) );
			if ( XxHash3.HashToUInt64( bytes.AsSpan( 0, payloadLength ) ) != expected ) return null;

			using var stream = new MemoryStream( bytes, 0, payloadLength, writable: false );
			using var reader = new BinaryReader( stream, Encoding.UTF8 );
			if ( reader.ReadUInt32() != IndexMagic || reader.ReadInt32() != IndexVersion ) return null;

			var snapshotTime = reader.ReadInt64();
			var count = reader.ReadInt32();
			if ( count < 0 ) return null;

			var entries = new Dictionary<string, IndexEntry>( count, StringComparer.OrdinalIgnoreCase );
			for ( var i = 0; i < count; i++ )
			{
				var name = reader.ReadString();
				var size = reader.ReadInt64();
				var time = reader.ReadInt64();
				var record = ReadRecordBinary( reader );
				entries[name] = new IndexEntry( size, time, record );
			}

			return stream.Position == payloadLength ? new RecordIndex( snapshotTime, entries ) : null;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or EndOfStreamException or InvalidDataException or FormatException )
		{
			return null;
		}
	}

	static void WriteRecord( BinaryWriter w, AssetRecord r )
	{
		w.Write( r.Source );
		w.Write( r.Job );
		w.Write( r.ChangedWhileCompiling );
		w.Write( r.Seconds );
		w.Write( r.Fingerprint );
		w.Write( r.Input is not null ? r.Input.Length : -1 );
		if ( r.Input is not null ) w.Write( r.Input );

		w.Write( r.Outputs.Count );
		foreach ( var (key, stamp) in r.Outputs )
		{
			w.Write( key );
			w.Write( stamp.Size );
			w.Write( stamp.Time );
			w.Write( stamp.Hash );
		}

		w.Write( r.Inputs.Count );
		foreach ( var input in r.Inputs )
		{
			w.Write( input.Path );
			w.Write( input.Exists );
			w.Write( input.Checksum );
			w.Write( input.Size );
			w.Write( input.Time );
		}

		w.Write( r.Special.Count );
		foreach ( var dep in r.Special )
		{
			w.Write( dep.Compiler );
			w.Write( dep.Data );
			w.Write( dep.Name );
			w.Write( dep.Fingerprint );
		}

		w.Write( r.Arguments.Count );
		foreach ( var (key, value) in r.Arguments )
		{
			w.Write( key );
			w.Write( value is not null );
			if ( value is not null ) w.Write( value );
		}

		w.Write( r.Prerequisites.Count );
		foreach ( var prereq in r.Prerequisites )
		{
			w.Write( prereq.File );
			w.Write( prereq.Hash );
			w.Write( prereq.Output is not null );
			if ( prereq.Output is not null ) w.Write( prereq.Output );
			w.Write( prereq.OrderOnly );
		}
	}

	static AssetRecord ReadRecordBinary( BinaryReader r )
	{
		var record = new AssetRecord
		{
			Source = r.ReadString(),
			Job = r.ReadString(),
			ChangedWhileCompiling = r.ReadBoolean(),
			Seconds = r.ReadDouble(),
			Fingerprint = r.ReadUInt32(),
		};

		var inputLen = r.ReadInt32();
		if ( inputLen < -1 ) throw new InvalidDataException();
		if ( inputLen >= 0 )
		{
			record.Input = r.ReadBytes( inputLen );
			if ( record.Input.Length != inputLen ) throw new EndOfStreamException();
		}

		var outputs = ReadCount( r );
		for ( var i = 0; i < outputs; i++ )
			record.Outputs[r.ReadString()] = new FileStamp( r.ReadInt64(), r.ReadInt64(), r.ReadString() );

		var inputs = ReadCount( r );
		record.Inputs.Capacity = inputs;
		for ( var i = 0; i < inputs; i++ )
			record.Inputs.Add( new InputStamp( r.ReadString(), r.ReadBoolean(), r.ReadUInt32(), r.ReadInt64(), r.ReadInt64() ) );

		var special = ReadCount( r );
		record.Special.Capacity = special;
		for ( var i = 0; i < special; i++ )
			record.Special.Add( new SpecialDependency( r.ReadString(), r.ReadUInt32(), r.ReadString(), r.ReadUInt32() ) );

		var arguments = ReadCount( r );
		for ( var i = 0; i < arguments; i++ )
		{
			var key = r.ReadString();
			record.Arguments[key] = r.ReadBoolean() ? r.ReadString() : null;
		}

		var prerequisites = ReadCount( r );
		record.Prerequisites.Capacity = prerequisites;
		for ( var i = 0; i < prerequisites; i++ )
		{
			var file = r.ReadString();
			var hash = r.ReadString();
			var output = r.ReadBoolean() ? r.ReadString() : null;
			var orderOnly = r.ReadBoolean();
			record.Prerequisites.Add( new RecordedPrerequisite( file, hash, output, orderOnly ) );
		}

		return record;
	}

	static int ReadCount( BinaryReader r )
	{
		var count = r.ReadInt32();
		if ( count < 0 ) throw new InvalidDataException();
		return count;
	}

	static void WriteJson<T>( string file, T value )
	{
		File.WriteAllText( file + ".tmp", JsonSerializer.Serialize( value, Json ) );
		File.Move( file + ".tmp", file, true );
	}

	/// <summary>
	/// Remove deferred root-local outputs only if they are still unowned and still the bytes their owner published.
	/// An interruption leaves the list on disk for the next build.
	/// </summary>
	public IEnumerable<string> RemoveDeferredOutputs()
	{
		if ( deferred is null || HasUnknownOwners ) yield break;

		foreach ( var file in DeletableOutputs() )
		{
			File.Delete( file );
			yield return file;
		}

		File.Delete( deferredFile );
		deferred = null;
	}

	/// <summary>
	/// Damaged records that prevent removal of an otherwise obsolete output in this root.
	/// </summary>
	public IEnumerable<(string Source, string DamagedRecord)> OwnersBlockingCleanup()
	{
		if ( deferred is null || !HasUnknownOwners || !DeletableOutputs().Any() ) yield break;

		foreach ( var source in unknownOwners )
			yield return (source, RecordPath( source ) + ".invalid");
	}

	IEnumerable<string> DeletableOutputs()
	{
		var owned = All.Values.SelectMany( record => record.Outputs.Keys ).Select( resolve ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		foreach ( var (key, hash) in deferred )
		{
			if ( key.Contains( ':' ) ) continue;
			var file = resolve( key );
			if ( file is null || owned.Contains( file ) || !File.Exists( file ) || Hashing.File( file ) != hash ) continue;

			yield return file;
		}
	}

	PublishFailedException Undo( string journal, Exception e )
	{
		Recover( journal );
		return new PublishFailedException( $"couldn't publish: {e.Message}", e );
	}

	/// <summary>
	/// Finishes or undoes a publication that was interrupted. The record decides: written means
	/// committed, so only the backup goes; otherwise every file goes back to how it was.
	/// </summary>
	void Recover( string journalFile )
	{
		var journal = JsonSerializer.Deserialize<Journal>( File.ReadAllText( journalFile ) );
		var backup = Path.Combine( backups, journal.Job );
		if ( !IsCommitted( journal ) )
		{
			foreach ( var move in Enumerable.Reverse( journal.Moves ) )
			{
				var saved = BackupPath( backup, move.Target );
				if ( File.Exists( saved ) ) MoveFile( saved, move.Target );
				else if ( !move.Existed && move.Staged is not null && File.Exists( move.Target ) ) File.Delete( move.Target );
			}
		}

		if ( Directory.Exists( backup ) ) Directory.Delete( backup, true );
		File.Delete( journalFile );
	}

	bool IsCommitted( Journal journal )
	{
		var file = RecordPath( journal.Source );
		if ( !File.Exists( file ) ) return journal.Remove;
		if ( journal.Remove ) return false;

		var marker = JsonSerializer.Deserialize<CommitMarker>( File.ReadAllText( file ), Json );
		if ( !string.Equals( marker?.Source, journal.Source, StringComparison.OrdinalIgnoreCase ) || !Guid.TryParseExact( marker?.Job, "N", out _ ) )
			throw new JsonException( $"Invalid commit marker: {file}" );

		return string.Equals( marker.Job, journal.Job, StringComparison.OrdinalIgnoreCase );
	}

	static string BackupPath( string backup, string target ) => Path.Combine( backup, $"{XxHash3.HashToUInt64( System.Text.Encoding.UTF8.GetBytes( Path.GetFullPath( target ).ToLowerInvariant() ) ):x16}" );

	static void MoveFile( string from, string to )
	{
		Directory.CreateDirectory( Path.GetDirectoryName( to ) );
		File.Move( from, to, true );
	}
}

static class Hashing
{
	public static FileStamp Stamp( string path )
	{
		var file = new FileInfo( path );
		return new FileStamp( file.Length, file.LastWriteTimeUtc.Ticks, File( path ) );
	}

	public static string File( string path )
	{
		using var stream = System.IO.File.OpenRead( path );
		var hash = new XxHash3();
		hash.Append( stream );
		return $"{hash.GetCurrentHashAsUInt64():x16}";
	}

	/// <summary>
	/// The checksum resourcecompiler records for an input: the low 32 bits of its XXH3-64.
	/// </summary>
	public static uint Checksum( string path )
	{
		using var stream = System.IO.File.OpenRead( path );
		var hash = new XxHash3();
		hash.Append( stream );
		return (uint)hash.GetCurrentHashAsUInt64();
	}
}
