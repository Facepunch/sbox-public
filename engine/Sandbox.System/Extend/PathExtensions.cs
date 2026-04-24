using System.IO;
using System.Text.RegularExpressions;

namespace Sandbox;

public static partial class SandboxSystemExtensions
{
	/// <summary>
	/// Resolves paths for the current platform. On Linux, performs case-insensitive segment walking.
	/// When <paramref name="resolveBasePath"/> is true, the first path is trusted as-is and only
	/// the remaining paths are walked. When false, all paths including the first are walked.
	/// Use only for full system to engine subroot paths
	/// </summary>
	public static string FindPlatformPath( bool resolveBasePath, params string[] parts )
	{
		if ( parts.Length == 0 ) return string.Empty;

		// set normalize to false to prevent a prepended / on C: root
		var combined = Path.Combine( parts ).NormalizeFilename( false );

		if ( !IsLinuxPlatform() )
			return combined;

		if ( Directory.Exists( combined ) || File.Exists( combined ) )
			return combined;

		var current = resolveBasePath ? parts[0] : string.Empty;
		var toWalk = resolveBasePath ? parts[1..] : parts;

		var resolved = ResolveCaseInsensitive( current, toWalk ) ?? combined;
		Log.Info( $"[FindPlatformPath] '{combined}' -> '{resolved}'" );
		return resolved;
	}

	/// <summary>
	/// Resolves paths for the current platform, treating the first path as a trusted base.
	/// </summary>
	public static string FindPlatformPath( params string[] parts ) => FindPlatformPath( true, parts );

	private static string ResolveCaseInsensitive( string current, string[] parts )
	{
		foreach ( var part in parts )
		{
			foreach ( var segment in part.Split( '/', StringSplitOptions.RemoveEmptyEntries ) )
			{
				if ( !Directory.Exists( current ) )
					return null;

				var match = Directory.EnumerateFileSystemEntries( current )
					.FirstOrDefault( e => string.Equals( Path.GetFileName( e ), segment, StringComparison.OrdinalIgnoreCase ) );

				if ( match is null ) return null;
				current = match;
			}
		}

		return current;
	}
}
