

namespace Sandbox;

/// <summary>
/// Manages BaseFileSystem calls on Linux machines. Takes case-insensitive NTFS paths and
/// rebuilds them to their case-sensitive variants.
/// </summary>
internal static partial class Platform
{
	internal static bool IsLinux() => SandboxSystemExtensions.IsLinuxPlatform(); // returns true if running a linux filesystem
	internal static System.Collections.Concurrent.ConcurrentDictionary<string, string> directoryCache = new( StringComparer.Ordinal ); // manages duplicate calls to same file

	internal static string BuildZioPath( Zio.IFileSystem system, string path )
	{
		// ui imports are often hardcoded lowercase. this checks for lowercase only files
		// and manages uppercase walks if needed. skip existing files.
		var original = path;
		path = path.ToLowerInvariant();

		if ( system.DirectoryExists( path ) || system.FileExists( path ) )
			return path;

		if ( directoryCache.TryGetValue( path, out var cached ) )
			return cached;

		var segments = path.Trim('/').Split( '/', StringSplitOptions.RemoveEmptyEntries );
		var resolved = ResolveCaseInsensitive( system, segments, path );

		if ( resolved != path )
		{
			directoryCache.TryAdd( path, resolved );
			Log.Info( $"[BuildZioPath] '{original}' -> '{resolved}'" );
		}

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

		return current.FullName;
	}
}
