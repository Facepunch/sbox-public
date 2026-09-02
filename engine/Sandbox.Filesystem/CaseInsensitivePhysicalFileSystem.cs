using System.Collections.Concurrent;
using System.IO;
using Zio;
using Zio.FileSystems;

namespace Sandbox;

/// <summary>
/// A physical filesystem that resolves paths case-insensitively on Linux.
/// </summary>
internal sealed class CaseInsensitivePhysicalFileSystem : PhysicalFileSystem
{
	/// <summary>
	/// Real directory path -> the names inside it.
	/// </summary>
	private readonly ConcurrentDictionary<string, DirectoryEntries> _directoryCache = new( StringComparer.Ordinal );

	/// <summary>
	/// Input path -> resolved path with correct on-disk casing.
	///
	/// Keyed exactly, not ignoring case: when two folders differ only by case they both really
	/// exist, and "code" and "Code" have to be able to resolve to different places.
	/// </summary>
	private readonly ConcurrentDictionary<string, string> _resolvedPathCache = new( StringComparer.Ordinal );

	protected override string ConvertPathToInternalImpl( UPath path )
	{
		return ResolvePathCasing( base.ConvertPathToInternalImpl( path ) );
	}

	/// <summary>
	/// Walk each component of <paramref name="path"/> and resolve it to the casing it has on
	/// disk. A component that already exists exactly as given is kept as-is, so a path that's
	/// already correct is never rewritten - that matters when two folders differ only by case,
	/// because otherwise we'd be free to pick the wrong one.
	///
	/// Resolving stops at the first component that doesn't exist and the rest of the path is
	/// appended untouched, which keeps this usable for paths that are about to be created and
	/// lets the OS produce a normal "file not found" for paths that never existed.
	/// </summary>
	/// <summary>
	/// Resolve a native path's casing without going through a filesystem instance - for code
	/// that hands an absolute path straight to System.IO. Shares one instance so the directory
	/// listings it builds are shared too.
	/// </summary>
	internal static string ResolveNativeCasing( string path )
	{
		if ( !OperatingSystem.IsLinux() )
			return path;

		return _shared.Value.ResolvePathCasing( path );
	}

	private static readonly Lazy<CaseInsensitivePhysicalFileSystem> _shared = new( () => new CaseInsensitivePhysicalFileSystem() );

	private string ResolvePathCasing( string path )
	{
		if ( path is null || path.Length < 2 )
			return path;

		if ( _resolvedPathCache.TryGetValue( path, out var cached ) )
			return cached;

		var components = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		var resolved = "/";

		for ( var i = 0; i < components.Length; i++ )
		{
			var entries = GetDirectoryEntries( resolved );

			if ( entries is null || !entries.TryResolve( components[i], out var realName ) )
			{
				// Nothing more we can say about this path - keep whatever is left as-is, and
				// don't cache it, it might well exist by the time we're asked again.
				return Append( resolved, string.Join( '/', components, i, components.Length - i ) );
			}

			resolved = Append( resolved, realName );
		}

		_resolvedPathCache.TryAdd( path, resolved );

		return resolved;
	}

	private static string Append( string directory, string name )
	{
		return directory == "/" ? $"/{name}" : $"{directory}/{name}";
	}

	/// <summary>
	/// The names inside <paramref name="directory"/>, or null if it doesn't exist.
	/// </summary>
	private DirectoryEntries GetDirectoryEntries( string directory )
	{
		if ( _directoryCache.TryGetValue( directory, out var entries ) )
			return entries;

		if ( !Directory.Exists( directory ) )
			return null;

		try
		{
			entries = new DirectoryEntries( new DirectoryInfo( directory ).GetFileSystemInfos() );

			_directoryCache.TryAdd( directory, entries );

			return entries;
		}
		catch
		{
			return null;
		}
	}

	//
	// Cache invalidation for mutations
	//

	private void InvalidateParent( string resolvedPath )
	{
		var parent = Path.GetDirectoryName( resolvedPath );

		if ( parent is not null )
			_directoryCache.TryRemove( parent, out _ );

		InvalidateResolvedPaths( parent ?? resolvedPath );
	}

	/// <summary>
	/// Remove resolved-path cache entries whose resolved value passes through
	/// <paramref name="directoryPrefix"/>.
	/// </summary>
	private void InvalidateResolvedPaths( string directoryPrefix )
	{
		foreach ( var kvp in _resolvedPathCache )
		{
			if ( kvp.Value.StartsWith( directoryPrefix, StringComparison.Ordinal ) )
				_resolvedPathCache.TryRemove( kvp.Key, out _ );
		}
	}

	protected override void CreateDirectoryImpl( UPath path )
	{
		base.CreateDirectoryImpl( path );
		var resolved = ConvertPathToInternal( path );
		InvalidateParent( resolved );
		_directoryCache.TryRemove( resolved, out _ );
	}

	protected override void DeleteDirectoryImpl( UPath path, bool isRecursive )
	{
		var resolved = ConvertPathToInternal( path );
		base.DeleteDirectoryImpl( path, isRecursive );
		InvalidateParent( resolved );
		_directoryCache.TryRemove( resolved, out _ );
	}

	protected override void DeleteFileImpl( UPath path )
	{
		var resolved = ConvertPathToInternal( path );
		base.DeleteFileImpl( path );
		InvalidateParent( resolved );
	}

	protected override void MoveDirectoryImpl( UPath srcPath, UPath destPath )
	{
		var resolvedSrc = ConvertPathToInternal( srcPath );
		base.MoveDirectoryImpl( srcPath, destPath );
		InvalidateParent( resolvedSrc );
		InvalidateParent( ConvertPathToInternal( destPath ) );
		_directoryCache.TryRemove( resolvedSrc, out _ );
	}

	protected override void MoveFileImpl( UPath srcPath, UPath destPath )
	{
		var resolvedSrc = ConvertPathToInternal( srcPath );
		base.MoveFileImpl( srcPath, destPath );
		InvalidateParent( resolvedSrc );
		InvalidateParent( ConvertPathToInternal( destPath ) );
	}

	protected override void CopyFileImpl( UPath srcPath, UPath destPath, bool overwrite )
	{
		base.CopyFileImpl( srcPath, destPath, overwrite );
		InvalidateParent( ConvertPathToInternal( destPath ) );
	}

	protected override Stream OpenFileImpl( UPath path, FileMode mode, FileAccess access, FileShare share )
	{
		var stream = base.OpenFileImpl( path, mode, access, share );

		if ( mode is FileMode.Create or FileMode.CreateNew or FileMode.OpenOrCreate )
			InvalidateParent( ConvertPathToInternal( path ) );

		return stream;
	}

	/// <summary>
	/// Clear all caches (e.g. after external file changes).
	/// </summary>
	internal void InvalidateCache()
	{
		_directoryCache.Clear();
		_resolvedPathCache.Clear();
	}

	/// <summary>
	/// The names in a single directory, looked up ignoring case.
	/// </summary>
	private sealed class DirectoryEntries
	{
		private readonly Dictionary<string, string> names;

		/// <summary>
		/// Names that lost the case-insensitive race above, so we can still find them by their
		/// exact spelling. Stays null for the overwhelming majority of directories, which don't
		/// contain two names differing only by case.
		/// </summary>
		private readonly HashSet<string> alsoSpelled;

		public DirectoryEntries( FileSystemInfo[] infos )
		{
			names = new Dictionary<string, string>( infos.Length, StringComparer.OrdinalIgnoreCase );

			foreach ( var info in infos )
			{
				if ( !names.TryAdd( info.Name, info.Name ) )
				{
					alsoSpelled ??= new HashSet<string>( StringComparer.Ordinal );
					alsoSpelled.Add( info.Name );
				}
			}
		}

		/// <summary>
		/// Find the real on-disk name for <paramref name="name"/>, preferring an exact match.
		/// </summary>
		public bool TryResolve( string name, out string realName )
		{
			if ( alsoSpelled is not null && alsoSpelled.Contains( name ) )
			{
				realName = name;
				return true;
			}

			return names.TryGetValue( name, out realName );
		}
	}
}
