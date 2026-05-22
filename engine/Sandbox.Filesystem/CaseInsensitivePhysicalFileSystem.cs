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
	/// Real directory path -> case-insensitive name lookup (name -> actual on-disk name).
	/// </summary>
	private readonly ConcurrentDictionary<string, Dictionary<string, string>> _directoryCache = new( StringComparer.Ordinal );

	/// <summary>
	/// Input path (case-insensitive key) -> resolved path with correct on-disk casing.
	/// </summary>
	private readonly ConcurrentDictionary<string, string> _resolvedPathCache = new( StringComparer.OrdinalIgnoreCase );

	protected override string ConvertPathToInternalImpl( UPath path )
	{
		return ResolvePathCasing( base.ConvertPathToInternalImpl( path ) );
	}

	/// <summary>
	/// Walk each component of <paramref name="path"/> and resolve it to the actual
	/// on-disk casing. Returns the original path if any component can't be matched,
	/// letting the OS produce a normal "file not found" error.
	/// </summary>
	private string ResolvePathCasing( string path )
	{
		if ( path.Length < 2 )
			return path;

		if ( _resolvedPathCache.TryGetValue( path, out var cached ) )
			return cached;

		var components = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		var resolvedDir = "/";

		for ( var i = 0; i < components.Length; i++ )
		{
			var entries = GetDirectoryEntries( resolvedDir );
			if ( entries is null || !entries.TryGetValue( components[i], out var realName ) )
				return path;

			resolvedDir = resolvedDir == "/"
				? $"/{realName}"
				: $"{resolvedDir}/{realName}";
		}

		_resolvedPathCache.TryAdd( path, resolvedDir );
		return resolvedDir;
	}

	/// <summary>
	/// Returns a case-insensitive lookup of names in <paramref name="directory"/>,
	/// mapping each name to its real on-disk casing. Returns null if the directory
	/// doesn't exist.
	/// </summary>
	private Dictionary<string, string> GetDirectoryEntries( string directory )
	{
		if ( _directoryCache.TryGetValue( directory, out var entries ) )
			return entries;

		if ( !Directory.Exists( directory ) )
			return null;

		try
		{
			var infos = new DirectoryInfo( directory ).GetFileSystemInfos();
			var lookup = new Dictionary<string, string>( infos.Length, StringComparer.OrdinalIgnoreCase );

			foreach ( var info in infos )
				lookup.TryAdd( info.Name, info.Name );

			_directoryCache.TryAdd( directory, lookup );
			return lookup;
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
}
