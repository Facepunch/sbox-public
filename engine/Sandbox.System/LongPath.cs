using System;
using System.IO;

namespace Sandbox;

/// <summary>
/// Helpers for working with Windows extended-length paths, which are not limited
/// by the legacy 260 character MAX_PATH. On other platforms the transformations
/// are no-ops and <see cref="MaxPathLength"/> is only a heuristic limit.
/// </summary>
public static class LongPath
{
	private const string ExtendedPrefix = @"\\?\";
	private const string UncExtendedPrefix = @"\\?\UNC\";
	private const string UncPrefix = @"\\";

	/// <summary>
	/// Longest path accepted by the path heuristics. Defaults to the Windows
	/// extended-length ceiling; exposed so developers can tune it.
	/// </summary>
	public static int MaxPathLength { get; set; } = 32767;

	/// <summary>
	/// Returns true when the path is longer than <see cref="MaxPathLength"/>.
	/// A null path is never considered too long.
	/// </summary>
	public static bool IsTooLong( string path )
	{
		return path is not null && path.Length > MaxPathLength;
	}

	/// <summary>
	/// Returns the extended-length form of an absolute Windows path. The input is
	/// returned unchanged on non-Windows platforms, for relative paths, for paths
	/// that are already prefixed, and for paths containing dot segments (the
	/// extended prefix disables path normalization).
	/// </summary>
	public static string EnsureExtendedPrefix( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) || !OperatingSystem.IsWindows() )
			return path;

		if ( path.StartsWith( ExtendedPrefix, StringComparison.Ordinal ) || path.StartsWith( UncExtendedPrefix, StringComparison.Ordinal ) )
			return path;

		foreach ( var segment in path.Split( '\\', '/' ) )
		{
			if ( segment == "." || segment == ".." )
				return path;
		}

		if ( !Path.IsPathRooted( path ) )
			return path;

		var fullPath = Path.GetFullPath( path ).Replace( '/', '\\' );

		if ( fullPath.StartsWith( UncPrefix, StringComparison.Ordinal ) )
			return UncExtendedPrefix + fullPath.Substring( UncPrefix.Length );

		return ExtendedPrefix + fullPath;
	}

	/// <summary>
	/// Removes the Windows extended-length prefix, including its UNC form.
	/// Paths without an extended prefix are returned unchanged.
	/// </summary>
	public static string RemoveExtendedPrefix( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return path;

		if ( path.StartsWith( UncExtendedPrefix, StringComparison.Ordinal ) )
			return UncPrefix + path.Substring( UncExtendedPrefix.Length );

		if ( path.StartsWith( ExtendedPrefix, StringComparison.Ordinal ) )
			return path.Substring( ExtendedPrefix.Length );

		return path;
	}
}
