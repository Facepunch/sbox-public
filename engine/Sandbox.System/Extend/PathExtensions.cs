using System.IO;
using System.Text.RegularExpressions;

namespace Sandbox;

public static partial class SandboxSystemExtensions
{
    /// <summary>
	/// Checks if the OS is Linux and assumes the filesystem is ext4
	/// </summary>
	/// <returns></returns>
	public static bool IsLinuxPlatform() => OperatingSystem.IsLinux();

	/// <summary>
	/// Puts a filename into the format /path/filename.ext (from path\FileName.EXT); excludes case-sensitive filesystems
	/// </summary>
	public static string NormalizeFilename( this string str, bool enforceInitialSlash = true ) => NormalizeFilename( str, enforceInitialSlash, true, '/' );

	/// <summary>
	/// Puts a filename into the format /path/filename.ext (from path\FileName.EXT; excludes case-sensitive filesystems
	/// </summary>
	public static string NormalizeFilename( this string str, bool enforceInitialSlash, bool enforceLowerCase, char targetSeparator = '/' )
	{
		if ( IsLinuxPlatform() ) enforceLowerCase = false;

		if ( str.Length == 0 )
		{
			return enforceInitialSlash ? string.Create( 1, targetSeparator, static ( span, sep ) => span[0] = sep ) : str;
		}

		var startsWithSeparator = str[0] == targetSeparator || str[0] == '/' || str[0] == '\\';
		var addLeadingSeparator = enforceInitialSlash && !startsWithSeparator;

		var resultLength = str.Length + (addLeadingSeparator ? 1 : 0);
		return string.Create( resultLength, (str, addLeadingSeparator, enforceLowerCase, targetSeparator), static ( span, state ) =>
		{
			var (source, addSep, lowerCase, sep) = state;
			var dest = 0;

			if ( addSep )
			{
				span[dest++] = sep;
			}

			for ( var i = 0; i < source.Length; i++ )
			{
				var c = source[i];

				if ( c == '/' || c == '\\' )
				{
					c = sep;
				}

				if ( lowerCase )
				{
					c = char.ToLowerInvariant( c );
				}

				span[dest++] = c;
			}
		} );
	}

	/// <summary>
	/// Adds or replaces the extension of <paramref name="path"/> to <paramref name="ext"/>.
	/// </summary>
	/// <param name="path">A file path with or without an extension.</param>
	/// <param name="ext">A file extension with or without a leading period.</param>
	/// <returns></returns>
	public static string WithExtension( this string path, string ext )
	{
		ArgumentNullException.ThrowIfNull( path, nameof( path ) );
		ArgumentNullException.ThrowIfNull( ext, nameof( ext ) );

		if ( !ext.StartsWith( '.' ) ) ext = $".{ext}";

		var curExt = Path.GetExtension( path );

		if ( string.Equals( curExt, ext, StringComparison.OrdinalIgnoreCase ) )
		{
			return path;
		}

		return $"{path[..^curExt.Length]}{ext}";
	}

	static Regex simplifyregex = new Regex( @"[^\\/]+(?<!\.\.)[\\/]\.\.[\\/]", RegexOptions.Compiled );

	/// <summary>
	/// Gets rid of ../'s (from /path/folder/../file.txt to /path/file.txt)
	/// </summary>
	/// <param name="str"></param>
	/// <returns></returns>
	public static string SimplifyPath( this string str )
	{
		while ( true )
		{
			var newPath = simplifyregex.Replace( str, "" );
			if ( newPath == str ) break;
			str = newPath;
		}
		return str;
	}

    private static char[] FilenameDelim = new[] { '/', '\\' };

	/// <summary>
	/// If the string is longer than this amount of characters then truncate it
	/// If appendage is defined, it will be appended to the end of truncated strings (ie, "..")
	/// </summary>
	public static string TruncateFilename( this string str, int maxLength, string appendage = null )
	{
		if ( string.IsNullOrEmpty( str ) ) return str;
		if ( str.Length <= maxLength ) return str;

		maxLength -= 3; //account for delimiter spacing

		string final;
		List<string> parts;

		int loops = 0;
		while ( loops++ < 100 )
		{
			parts = str.Split( FilenameDelim ).ToList();
			parts.RemoveRange( parts.Count - 1 - loops, loops );
			if ( parts.Count == 1 )
			{
				return parts.Last();
			}

			parts.Insert( parts.Count - 1, "..." );
			final = string.Join( "/", parts.ToArray() );
			if ( final.Length < maxLength )
			{
				return final;
			}
		}

		return str.Split( FilenameDelim ).ToList().Last();
	}

    /// <summary>
	/// Make the passed in string filename safe. This replaces any invalid characters with "_".
	/// </summary>
	public static string GetFilenameSafe( this string input )
	{
		// Get the array of invalid characters
		char[] invalidChars = Path.GetInvalidFileNameChars();

		// Replace invalid characters with an underscore
		return new string( input.Select( ch => invalidChars.Contains( ch ) ? '_' : ch ).ToArray() );
	}


	/// <summary>
	/// Resolves partial paths on a root for Windows and non-Windows operating systems.
	/// </summary>
	/// <param name="parts">First argument must be a root path.</param>
    public static string FindPlatformPath( params string[] parts ) => FindPlatformPath( true, parts );

	/// <summary>
	/// Resolves paths for Windows and non-Windows operating systems.
	/// Use to segment walk a full path instead of partial only.
	/// </summary>
	public static string FindPlatformPath( bool enforceBasePath = true, params string[] parts )
	{
        // you need to make it so that the first argument is a rootpath
        // its safer and doesn't need to be walked up
        // only the partial paths needs to be managed properly
		// Normalize filename takes care of \\ to /
		// set normalize to false to prevent a prepended / on C: root
		var combined = Path.Combine( parts ).NormalizeFilename( false ); 

		if ( !IsLinuxPlatform() )
			return combined;

		if ( Directory.Exists( combined ) || File.Exists( combined ) )
			return combined;

		var resolved = ResolveCaseInsensitive( enforceBasePath, parts ) ?? combined;
		Log.Info( $"[FindPlatformPath] '{combined}' -> '{resolved}'" );
		return resolved;
	}

	private static string ResolveCaseInsensitive( bool enforceBasePath, params string[] parts )
	{
		// enforcing a base path prevents a full walk
		// means first string option will not be walked,
		// and only the partial path is walked in sbox-public directory
		if ( parts.Length == 0 ) return null;

		var current = enforceBasePath ? parts[0] : string.Empty;
		var partials = enforceBasePath ? parts.AsSpan( 1 ) : parts.AsSpan();

		foreach ( var part in partials )
		{
			var segments = part.ToLowerInvariant().Split( '/', StringSplitOptions.RemoveEmptyEntries );

			foreach ( var segment in segments )
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