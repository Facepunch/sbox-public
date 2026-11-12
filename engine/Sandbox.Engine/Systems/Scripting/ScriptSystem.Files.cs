namespace Sandbox;

public sealed partial class ScriptSystem
{
	private Dictionary<string, string> _namedFiles = new( StringComparer.Ordinal );
	private readonly Dictionary<string, Script.ScriptError> _fileStartupErrors = new( StringComparer.Ordinal );
	private Script.ScriptError _fileLoadError;

	/// <summary>
	/// Outcome of a named-file update. Publication and startup are separate: startup failure does not undo
	/// published definitions or earlier side effects. Rejected preparation leaves the previous generation intact.
	/// </summary>
	public sealed class FileLoadResult
	{
		/// <summary>
		/// Whether the requested files are published and have no outstanding diagnostics or startup failures.
		/// </summary>
		public bool Success { get; internal init; }

		/// <summary>
		/// Whether this call committed a new generation, even if subsequent migration or startup failed.
		/// </summary>
		public bool Published { get; internal init; }

		/// <summary>
		/// Whether this scripting context must be restarted before further execution.
		/// </summary>
		public bool RequiresReload { get; internal init; }

		/// <summary>
		/// Compilation, publication and startup diagnostics, with individual source filenames retained.
		/// </summary>
		public IReadOnlyList<Script.ScriptError> Diagnostics { get; internal init; }
	}

	/// <summary>
	/// Reconciles the complete named-file set in one publication. Changed files are added or replaced;
	/// omitted files and their declarations are removed. Unchanged files retain their execution state.
	/// File identities are case-sensitive and must be nonblank. Sources are compiled independently, preserving
	/// their locations and per-file size limits. Do not mix named-file ownership with Load's anonymous chunks.
	/// Changed files run their top-level statements in ordinal filename order after all definitions publish.
	/// Startup side effects are not transactional; inspect Published and RequiresReload when Success is false.
	/// Clients receiving the host's scripts cannot replace the file set locally.
	/// </summary>
	public FileLoadResult LoadFiles( IReadOnlyDictionary<string, string> files )
	{
		CheckSourceAccess();
		return LoadFiles( files, null );
	}

	/// <summary>
	/// Adds or replaces one named script from source without reading or writing a file, preserving other scripts.
	/// The name shares the same namespace as loaded resource paths; a later resource scan can replace it.
	/// Hosts replicate .csx names except those ending in .server.csx, including to clients that join later.
	/// Unchanged source does not run again. Clients receiving the host's scripts cannot call this method.
	/// Publication and startup failures are reported in the result, as with <see cref="LoadFiles(IReadOnlyDictionary{string, string})"/>.
	/// </summary>
	public FileLoadResult LoadFile( string filePath, string source )
	{
		CheckSourceAccess();
		ArgumentException.ThrowIfNullOrWhiteSpace( filePath );
		ArgumentNullException.ThrowIfNull( source );

		var files = new Dictionary<string, string>( _namedFiles, StringComparer.Ordinal )
		{
			[filePath] = source
		};

		return LoadFiles( files, null );
	}

	/// <summary>
	/// Removes a named script and its declarations without deleting a file or undoing previous side effects.
	/// Hosts replicate shared-script removals. Other scripts retain their state, and a missing name is a no-op.
	/// Clients receiving the host's scripts cannot call this method. A later resource scan can restore a disk-backed script.
	/// Publication failures are reported in the result, as with <see cref="LoadFiles(IReadOnlyDictionary{string, string})"/>.
	/// </summary>
	public FileLoadResult RemoveFile( string filePath )
	{
		CheckSourceAccess();
		ArgumentException.ThrowIfNullOrWhiteSpace( filePath );

		var files = new Dictionary<string, string>( _namedFiles, StringComparer.Ordinal );
		files.Remove( filePath );

		return LoadFiles( files, null );
	}

	/// <summary>
	/// Resource scans explicitly restart the supplied files, including unchanged source, while ordinary edits
	/// preserve unchanged file state. Both paths publish one coherent declaration generation.
	/// </summary>
	private FileLoadResult LoadFiles( IReadOnlyDictionary<string, string> files, IReadOnlySet<string> restart )
	{
		CheckAccess();
		ArgumentNullException.ThrowIfNull( files );
		var snapshot = new Dictionary<string, string>( StringComparer.Ordinal );
		foreach ( var (path, source) in files )
		{
			ArgumentException.ThrowIfNullOrWhiteSpace( path );
			ArgumentNullException.ThrowIfNull( source );
			snapshot.Add( path, source );
		}

		_fileLoadError = null;
		var revision = Module.Revision;
		try
		{
			var changes = new List<Breen.ScriptFileChange>();
			var changedPaths = new List<string>();
			foreach ( var (path, source) in snapshot.OrderBy( x => x.Key, StringComparer.Ordinal ) )
			{
				var exists = _namedFiles.TryGetValue( path, out var previous );
				if ( exists && previous == source && restart?.Contains( path ) != true ) continue;
				var document = Breen.ScriptDocument.Parse( source, path );
				changes.Add( exists ? Breen.ScriptFileChange.Replace( document ) : Breen.ScriptFileChange.Add( document ) );
				changedPaths.Add( path );
			}

			foreach ( var path in _namedFiles.Keys.Except( snapshot.Keys ) )
			{
				changes.Add( Breen.ScriptFileChange.Remove( path ) );
			}

			if ( changes.Count > 0 )
			{
				Module.StopWarmup();
				Module.SetStartupStore( "local", new() );
				var publication = Module.ApplyBatch( changes );
				_namedFiles = snapshot;
				PublishNetworkFiles( sendUpdates: true );
				foreach ( var change in changes ) _fileStartupErrors.Remove( change.FileId );
				foreach ( var path in changedPaths )
				{
					var result = Module.RunStartup( publication, path );
					if ( result.Error is { } error ) _fileStartupErrors[path] = error.ToEngineError();
				}
				WarmMethods();
			}
		}
		catch ( Exception exception ) when ( exception is not OutOfMemoryException )
		{
			var frame = Breen.ScriptStackTrace.GetFrames( exception ).FirstOrDefault();
			_fileLoadError = new( exception is Breen.ScriptException fault ? fault.Code : "SCR_LOAD", exception.Message,
				frame?.Span.Start ?? 0, frame?.Span.Length ?? 0, frame?.FilePath ?? "", frame?.Line ?? 0, frame?.Column ?? 0 );
		}

		var diagnostics = Diagnostics;
		return new FileLoadResult
		{
			Success = !RequiresReload && diagnostics.Count == 0,
			Published = Module.Revision != revision,
			RequiresReload = RequiresReload,
			Diagnostics = diagnostics
		};
	}
}
