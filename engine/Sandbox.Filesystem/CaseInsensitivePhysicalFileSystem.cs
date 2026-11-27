using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
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

	/// <summary>
	/// Input path -> what it resolved to when some part of it didn't exist and the part that did was cased differently,
	/// with the listing of the deepest folder that existed. A mounted file system is asked for every file the others have,
	/// so most lookups during a load are misses. A miss already cased as on disk walks again without making anything, so
	/// only these, which would make a new string each time, are kept. One stays good while that folder's listing is still
	/// the cached one and the folder hasn't changed on disk since (a missing name appearing there changes its write time).
	/// </summary>
	private readonly ConcurrentDictionary<string, Miss> _missCache = new( StringComparer.Ordinal );

	private sealed record Miss( string Resolved, DirectoryEntries Deepest );

	/// <summary>Misses kept, counted here because a concurrent dictionary's Count takes every lock.</summary>
	private int _misses;

	/// <summary>More misses than this and they're all dropped: paths probed once stay out of memory.</summary>
	internal const int MaxMisses = 64 * 1024;

	private readonly ConcurrentDictionary<string, DirectoryEntries>.AlternateLookup<ReadOnlySpan<char>> _directoryBySpan;

	public CaseInsensitivePhysicalFileSystem()
	{
		_directoryBySpan = _directoryCache.GetAlternateLookup<ReadOnlySpan<char>>();
	}

	protected override string ConvertPathToInternalImpl( UPath path )
	{
		return ResolvePathCasing( base.ConvertPathToInternalImpl( path ) );
	}

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

	/// <summary>
	/// Walk each component of <paramref name="path"/> and resolve it to the casing it has on
	/// disk. A component that already exists exactly as given is kept as-is, so a path that's
	/// already correct is never rewritten - that matters when two folders differ only by case,
	/// because otherwise we'd be free to pick the wrong one.
	///
	/// Resolving stops at the first component that doesn't exist and the rest of the path is
	/// appended untouched, which keeps this usable for paths that are about to be created and
	/// lets the OS produce a normal "file not found" for paths that never existed.
	///
	/// The walk reads the path in place: the only string it makes is the result, and none when that is the path itself.
	/// </summary>
	internal string ResolvePathCasing( string path )
	{
		if ( path is null || path.Length < 2 )
			return path;

		if ( _resolvedPathCache.TryGetValue( path, out var cached ) )
			return cached;

		if ( _missCache.TryGetValue( path, out var miss ) )
		{
			if ( miss.Deepest.IsCached( this ) && miss.Deepest.IsCurrent() )
				return miss.Resolved;

			_missCache.TryRemove( path, out _ );
		}

		// The resolved path never grows: an OrdinalIgnoreCase match is as long as the name it matched
		char[] rented = null;
		Span<char> buffer = path.Length <= 512 ? stackalloc char[path.Length] : (rented = ArrayPool<char>.Shared.Rent( path.Length ));

		try
		{
			return Walk( path, buffer );
		}
		finally
		{
			if ( rented is not null )
				ArrayPool<char>.Shared.Return( rented );
		}
	}

	private string Walk( string path, Span<char> buffer )
	{
		var length = 0;
		var at = 0;

		while ( NextComponent( path, ref at, out var component ) )
		{
			var directory = length == 0 ? "/".AsSpan() : buffer[..length];
			var entries = GetDirectoryEntries( directory );

			if ( entries is null || !entries.TryResolve( component, out var realName ) )
			{
				entries = GetDirectoryEntries( directory, missed: true );
				if ( entries is null || !entries.TryResolve( component, out realName ) )
				{
					// Nothing more we can say about this path - keep whatever is left as-is. Kept as a miss while the
					// folder it stopped in is unchanged: it might well exist by the time we're asked again
					var rest = at - component.Length;
					while ( NextComponent( path, ref rest, out var left ) )
						Append( buffer, ref length, left );

					var missed = Result( path, buffer[..length] );
					if ( entries is not null && !ReferenceEquals( missed, path ) )
						KeepMiss( path, missed, entries );

					return missed;
				}
			}

			Append( buffer, ref length, realName );
		}

		var resolved = Result( path, buffer[..length] );
		_resolvedPathCache.TryAdd( path, resolved );

		return resolved;
	}

	private void KeepMiss( string path, string resolved, DirectoryEntries deepest )
	{
		if ( Interlocked.Increment( ref _misses ) > MaxMisses )
		{
			_missCache.Clear();
			Interlocked.Exchange( ref _misses, 0 );
		}

		_missCache[path] = new Miss( resolved, deepest );
	}

	/// <summary>The next name in <paramref name="path"/> from <paramref name="at"/>, skipping separators; false at its end.</summary>
	private static bool NextComponent( string path, ref int at, out ReadOnlySpan<char> component )
	{
		while ( at < path.Length && path[at] == '/' )
			at++;

		var start = at;
		while ( at < path.Length && path[at] != '/' )
			at++;

		component = path.AsSpan( start, at - start );
		return at > start;
	}

	private static void Append( Span<char> buffer, ref int length, ReadOnlySpan<char> name )
	{
		buffer[length++] = '/';
		name.CopyTo( buffer[length..] );
		length += name.Length;
	}

	/// <summary>The resolved path: <paramref name="path"/> itself when it was already that, else a new string.</summary>
	private static string Result( string path, ReadOnlySpan<char> resolved )
	{
		if ( resolved.IsEmpty )
			return "/";

		return resolved.SequenceEqual( path ) ? path : new string( resolved );
	}

	/// <summary>
	/// The names inside <paramref name="directory"/>, or null if it doesn't exist. A cached listing is read again once
	/// the directory has changed since: files written straight through System.IO, or by another process, never pass the
	/// mutation overrides below, and the shared instance behind <see cref="ResolveNativeCasing"/> sees no writes at all.
	/// Only asked on a miss, so a path that resolves costs nothing more.
	/// </summary>
	private DirectoryEntries GetDirectoryEntries( ReadOnlySpan<char> directory, bool missed = false )
	{
		if ( _directoryBySpan.TryGetValue( directory, out var entries ) && (!missed || entries.IsCurrent()) )
			return entries;

		var path = directory.ToString();
		if ( !Directory.Exists( path ) )
			return null;

		try
		{
			// Its write time first: a change made while it's listed reads it again next time
			var written = Directory.GetLastWriteTimeUtc( path );
			entries = new DirectoryEntries( path, new DirectoryInfo( path ).GetFileSystemInfos(), written );

			_directoryCache[path] = entries;

			return entries;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// After a path the cache resolved turns out not to be there: drop what it was resolved from, so a file or folder
	/// renamed outside the engine (only its case changed, which keeps its folder's listing matching) is found again.
	/// True when there was a resolved path to drop, and the lookup is worth asking again.
	/// </summary>
	private bool DropStale( UPath path )
	{
		if ( !_resolvedPathCache.TryRemove( base.ConvertPathToInternalImpl( path ), out var stale ) )
			return false;

		InvalidateParent( stale );
		return true;
	}

	protected override bool FileExistsImpl( UPath path )
	{
		return base.FileExistsImpl( path ) || (DropStale( path ) && base.FileExistsImpl( path ));
	}

	protected override bool DirectoryExistsImpl( UPath path )
	{
		return base.DirectoryExistsImpl( path ) || (DropStale( path ) && base.DirectoryExistsImpl( path ));
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

		// Creating a/b/c can create a and b too, so every listing above the new folder may be missing one
		for ( var directory = resolved; directory is not null; directory = Path.GetDirectoryName( directory ) )
			_directoryCache.TryRemove( directory, out _ );
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
		_missCache.Clear();
	}

	/// <summary>
	/// The names in a single directory, looked up ignoring case.
	/// </summary>
	private sealed class DirectoryEntries
	{
		private readonly Dictionary<string, string> names;
		private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> namesBySpan;

		/// <summary>The directory listed.</summary>
		public string Path { get; }

		/// <summary>
		/// Names that lost the case-insensitive race above, so we can still find them by their
		/// exact spelling. Stays null for the overwhelming majority of directories, which don't
		/// contain two names differing only by case.
		/// </summary>
		private readonly HashSet<string> alsoSpelled;
		private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> alsoSpelledBySpan;

		/// <summary>
		/// The directory's write time when it was listed: an entry added, removed or renamed since changes it.
		/// </summary>
		private readonly DateTime written;

		public DirectoryEntries( string path, FileSystemInfo[] infos, DateTime written )
		{
			Path = path;
			this.written = written;
			names = new Dictionary<string, string>( infos.Length, StringComparer.OrdinalIgnoreCase );

			foreach ( var info in infos )
			{
				if ( !names.TryAdd( info.Name, info.Name ) )
				{
					alsoSpelled ??= new HashSet<string>( StringComparer.Ordinal );
					alsoSpelled.Add( info.Name );
				}
			}

			namesBySpan = names.GetAlternateLookup<ReadOnlySpan<char>>();
			if ( alsoSpelled is not null )
				alsoSpelledBySpan = alsoSpelled.GetAlternateLookup<ReadOnlySpan<char>>();
		}

		/// <summary>
		/// Whether the directory still holds what was listed.
		/// </summary>
		public bool IsCurrent() => Directory.GetLastWriteTimeUtc( Path ) == written;

		/// <summary>Whether this is still <paramref name="fs"/>'s listing of its directory (none of its changes dropped it).</summary>
		public bool IsCached( CaseInsensitivePhysicalFileSystem fs ) => fs._directoryCache.TryGetValue( Path, out var cached ) && ReferenceEquals( cached, this );

		/// <summary>
		/// Find the real on-disk name for <paramref name="name"/>, preferring an exact match.
		/// </summary>
		public bool TryResolve( ReadOnlySpan<char> name, out ReadOnlySpan<char> realName )
		{
			if ( alsoSpelled is not null && alsoSpelledBySpan.Contains( name ) )
			{
				realName = name;
				return true;
			}

			if ( namesBySpan.TryGetValue( name, out var found ) )
			{
				realName = found;
				return true;
			}

			realName = default;
			return false;
		}
	}
}
