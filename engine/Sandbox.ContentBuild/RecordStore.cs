using System.IO.Hashing;
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
	readonly string records, journals, backups, deferredFile;
	Dictionary<string, string> deferred;
	readonly HashSet<string> quarantined;
	readonly HashSet<string> unknownOwners = new( StringComparer.OrdinalIgnoreCase );

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
		if ( File.Exists( deferredFile ) ) deferred = ReadCache( deferredFile, ReadDeferred );

		// A journal still being written was never renamed, so nothing it lists was moved
		foreach ( var partial in Directory.GetFiles( journals, "*.tmp" ) ) File.Delete( partial );
		foreach ( var journal in Directory.GetFiles( journals, "*.json" ) ) Recover( journal );

		var files = Directory.GetFiles( records, "*.json" );
		var loaded = new AssetRecord[files.Length];
		Func<string, AssetRecord> read = ReadRecord;
		Parallel.For( 0, files.Length, i => loaded[i] = ReadCache( files[i], read ) );
		foreach ( var record in loaded )
		{
			if ( record is not null ) All[record.Source] = record;
		}

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
			unknownOwners.Add( source );
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

		if ( record is null ) All.Remove( source );
		else All[source] = record;
		unknownOwners.Remove( source );

		File.Delete( journal );
		if ( Directory.Exists( backup ) ) Directory.Delete( backup, true );
		return moves.Select( x => x.Target ).ToList();
	}

	/// <summary>
	/// Writes a record again with nothing to move, as when its outputs' stamps moved but their bytes didn't.
	/// </summary>
	public void Rewrite( string source, AssetRecord record ) => WriteJson( RecordPath( source ), record );

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
