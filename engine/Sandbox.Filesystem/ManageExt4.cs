using System.IO;

namespace Sandbox;

/// <summary>
/// This class is for managing any operating systems that use case-insensitive filesystems such as Linux.
/// This is its own class so that if adaptations are needed in the future they can exclusively be done
/// here without having the methods be internally used in the BaseFileSystem, so we can maintain a
/// clear separation between Facepunch's source code and be as unaggressive as possible
/// with any Linux related changes that are approved.
/// </summary>
internal static class ManageExt4
{
	/// <summary>
	/// Linux filesystems cannot be resolved because the engine
	/// attempts to compare a lowercase path to an uppercase one. This rebuilds
	/// the file paths in segments to allow the engine to build the uppercase
	/// variant for Zio file systems.
	/// </summary>
	internal static string ResolveLinuxFilePath( Zio.IFileSystem system, string path )
	{
		if ( system is null ) return path;

		if ( system.DirectoryExists( path ) || system.FileExists( path ) )
			return path;

		var segments = path.Trim( '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries );
		var resolvedSegments = new List<string>( segments.Length );

		foreach ( var segment in segments )
		{
			// rebuild
			var ext4Path = new Zio.UPath( "/" + string.Join( "/", resolvedSegments ) );

			try
			{
				// enumerate upath
				var resolved = system
					.EnumeratePaths( ext4Path, "*", SearchOption.TopDirectoryOnly, Zio.SearchTarget.Directory )
					.Select( p => p.FullName.Substring( p.FullName.LastIndexOf( '/' ) + 1 ) )
					.FirstOrDefault( name => string.Equals( name, segment, StringComparison.OrdinalIgnoreCase ) )
					?? segment;

				//Log.Info( $"[ResolveLinuxFilePath] '{segment}' -> '{resolved}' (parent: {ext4Path})" );
				resolvedSegments.Add( resolved );
			}
			catch ( System.IO.DirectoryNotFoundException ) // Files return fine, case sensitive paths dont.
			{
				resolvedSegments.Add( segment );
			}
		}

		// The enumerated uppercase variants are reconstructed for the result
		var result = "/" + string.Join( "/", resolvedSegments );

		return result;
	}
}
