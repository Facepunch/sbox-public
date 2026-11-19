namespace Sandbox;

public sealed partial class ScriptSystem
{
	/// <summary>
	/// Publishes a resource scan or watcher notification together, preserving files outside that notification.
	/// File-local compile errors are logged and excluded before retrying the remaining candidate. A rejected
	/// replacement retains its last accepted source. Publication and migration failures are never retried.
	/// Promoted hosts do not start server-only sources from package scans or file watchers.
	/// </summary>
	internal void LoadResourceFiles( IReadOnlyDictionary<string, string> sources, IReadOnlyCollection<string> removed = null )
	{
		CheckAccess();
		if ( _context.ReceivesNetworkScripts )
		{
			return;
		}

		if ( sources.Count == 0 && (removed is null || removed.Count == 0) )
		{
			return;
		}

		var snapshot = new Dictionary<string, string>( _namedFiles, StringComparer.Ordinal );
		var restart = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var (path, source) in sources )
		{
			if ( _wasNetworkClient && !IsSharedFile( path ) )
			{
				continue;
			}

			snapshot[path] = source;
			restart.Add( path );
		}
		if ( removed is not null )
		{
			foreach ( var path in removed )
			{
				snapshot.Remove( path );
			}
		}

		// Files published while still waiting on a missing type have never been compiled. The module lists
		// those first and then its most recent chunk error, which is not a waiting file, so leave that out.
		var waiting = Module.Diagnostics.SkipLast( Module.LastError is null ? 0 : 1 )
			.Select( diagnostic => diagnostic.ToEngineError().FilePath ).ToHashSet( StringComparer.Ordinal );

		while ( true )
		{
			var result = LoadFiles( snapshot, restart );
			if ( result.Success )
			{
				return;
			}
			foreach ( var diagnostic in result.Diagnostics )
			{
				Log.Warning( $"{diagnostic.FilePath}: {diagnostic}" );
			}
			if ( result.Published || result.RequiresReload )
			{
				return;
			}

			// Only a source-located rejection can be isolated safely. Each retry removes one requested edit.
			var rejected = result.Diagnostics.FirstOrDefault( diagnostic => restart.Contains( diagnostic.FilePath ) );
			if ( rejected is null )
			{
				// This batch supplied the type a waiting file needed, and that file then failed its first
				// compile. Drop it instead of discarding the valid edits it was waiting for. Its source leaves
				// the module until the file is edited again; the warning logged above is the only trace.
				if ( _fileLoadError?.FilePath is { } failed && waiting.Remove( failed ) && snapshot.Remove( failed ) )
				{
					continue;
				}

				return;
			}
			var path = rejected.FilePath;
			restart.Remove( path );
			if ( _namedFiles.TryGetValue( path, out var previous ) )
			{
				snapshot[path] = previous;
			}
			else
			{
				snapshot.Remove( path );
			}
			if ( restart.Count == 0 && (removed is null || removed.Count == 0) )
			{
				return;
			}
		}
	}
}
