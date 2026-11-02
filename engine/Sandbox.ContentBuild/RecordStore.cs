using System.IO.Hashing;
using System.Text.Json;

namespace Sandbox.ContentBuild;

/// <summary>
/// The last successful build of one source asset: everything needed to tell whether its outputs are
/// still current, and which outputs it owns. Written only after those outputs are in place.
/// </summary>
public sealed class AssetRecord
{
	public string Source { get; set; }
	public string Job { get; set; }
	public Dictionary<string, FileStamp> Outputs { get; set; } = new( StringComparer.OrdinalIgnoreCase );
	public List<InputStamp> Inputs { get; set; } = [];
	public List<SpecialDependency> Special { get; set; } = [];
	public Dictionary<string, string> Arguments { get; set; } = [];
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
	readonly string records, journals, backups;
	static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

	public Dictionary<string, AssetRecord> All { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <param name="resolve">Where an output key is on disk, or null if its root isn't mounted in this build.</param>
	public RecordStore( Func<string, string> resolve, string state )
	{
		this.resolve = resolve;
		records = Directory.CreateDirectory( Path.Combine( state, "records" ) ).FullName;
		journals = Directory.CreateDirectory( Path.Combine( state, "journal" ) ).FullName;
		backups = Path.Combine( state, "backup" );

		// A journal still being written was never renamed, so nothing it lists was moved
		foreach ( var partial in Directory.GetFiles( journals, "*.tmp" ) ) File.Delete( partial );
		foreach ( var journal in Directory.GetFiles( journals, "*.json" ) ) Recover( journal );

		foreach ( var file in Directory.GetFiles( records, "*.json" ) )
		{
			var record = JsonSerializer.Deserialize<AssetRecord>( File.ReadAllText( file ) );
			All[record.Source] = record;
		}
	}

	public IEnumerable<string> Owners( string output ) => All.Values.Where( x => x.Outputs.ContainsKey( output ) ).Select( x => x.Source );

	string RecordPath( string source ) => Path.Combine( records, $"{XxHash3.HashToUInt64( System.Text.Encoding.UTF8.GetBytes( source.ToLowerInvariant() ) ):x16}.json" );

	sealed record Move( string Target, string Staged, bool Existed );
	sealed record Journal( string Job, string Source, bool Remove, List<Move> Moves );

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
		foreach ( var rel in obsolete )
		{
			var target = resolve( rel );
			if ( target is not null && File.Exists( target ) ) moves.Add( new Move( target, null, true ) );
		}

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

			if ( record is null )
			{
				File.Delete( RecordPath( source ) );
			}
			else
			{
				var temp = RecordPath( source ) + ".tmp";
				File.WriteAllText( temp, JsonSerializer.Serialize( record, Json ) );
				File.Move( temp, RecordPath( source ), true );
			}
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			throw Undo( journal, e );
		}

		if ( record is null ) All.Remove( source );
		else All[source] = record;

		File.Delete( journal );
		if ( Directory.Exists( backup ) ) Directory.Delete( backup, true );
		return moves.Select( x => x.Target ).ToList();
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
		var recordFile = RecordPath( journal.Source );
		var committed = journal.Remove ? !File.Exists( recordFile ) : File.Exists( recordFile ) && JsonSerializer.Deserialize<AssetRecord>( File.ReadAllText( recordFile ) ).Job == journal.Job;

		var backup = Path.Combine( backups, journal.Job );
		if ( !committed )
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
