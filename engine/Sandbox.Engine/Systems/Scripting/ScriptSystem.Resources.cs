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
