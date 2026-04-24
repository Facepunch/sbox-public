

namespace Sandbox;

/// <summary>
/// Manages BaseFileSystem calls on Linux machines. Takes case-insensitive NTFS paths and
/// rebuilds them to their case-sensitive variants.
/// </summary>
internal static partial class Platform
{
	private static System.Collections.Concurrent.ConcurrentDictionary<string, string> directoryCache = new( StringComparer.Ordinal ); // manages duplicate calls to same file
	private static bool PathExists( Zio.IFileSystem system, string path ) => (system.DirectoryExists( path ) || system.FileExists( path )) ? true : false;
	internal static bool IsLinuxPlatform() => SandboxSystemExtensions.IsLinuxPlatform(); // returns true if running a linux filesystem

	/// <summary>
	/// Rebuilds false missing file-paths by finding their uppercase variant
	/// on case-sensitive Linux machines
	/// </summary>
	/// <param name="system"></param>
	/// <param name="path"></param>
	/// <returns></returns>
	internal static string BuildZioPath( Zio.IFileSystem system, string path )
	{
		path = path.NormalizeFilename( true );

		if ( PathExists( system, path ) )
			return path;

		if ( directoryCache.TryGetValue( path, out var cached ) )
			return cached;

		var segments = path.Trim( '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries );
		var resolved = ResolveCaseInsensitive( system, segments, path );

		if ( !directoryCache.ContainsKey( path ) && PathExists( system, resolved ) ) // don't cache missing paths
			directoryCache.TryAdd( path, resolved );

		// return all paths, even nonexistent. possible directory or file creation
		return resolved;
	}

	private static string ResolveCaseInsensitive( Zio.IFileSystem system, string[] segments, string fallback )
	{
		var current = Zio.UPath.Root;

		foreach ( var segment in segments )
		{
			var match = system.EnumeratePaths( current, "*", System.IO.SearchOption.TopDirectoryOnly, Zio.SearchTarget.Both )
				.FirstOrDefault( e => string.Equals( System.IO.Path.GetFileName( e.FullName ), segment, StringComparison.OrdinalIgnoreCase ) );

			if ( match == default )
				return fallback;

			current = match;
		}

		return current.FullName.NormalizeFilename();
	}
}
