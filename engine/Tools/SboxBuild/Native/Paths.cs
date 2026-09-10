using System.Text.RegularExpressions;

namespace Facepunch.Native;

/// <summary>
/// All module paths are stored relative to src/ with forward slashes.
/// </summary>
public static class Paths
{
	/// <summary>Absolute path to src/.</summary>
	public static string SrcDir { get; set; } = FindSrcDir();

	/// <summary>The platform as paths spell it, which is not always what the platform is called.</summary>
	public static string Platform => NativePlatform.Current.DirectoryName;
	public static string LibPublic => NativePlatform.Current.LibPublic;
	public static string LibCommon => NativePlatform.Current.LibCommon;
	public static string BinDir => $"../game/bin/{Platform}";
	public static string ToolsDir => $"{BinDir}/tools";
	public static string DevToolsDir => $"devtools/bin/{Platform}";

	public static string Absolute( string srcRelative ) => Path.GetFullPath( Path.Combine( SrcDir, srcRelative ) );

	public static string ToSrcRelative( string absolute )
	{
		var relative = Path.GetRelativePath( SrcDir, absolute );
		return relative.Replace( '\\', '/' ).TrimEnd( '/' );
	}

	/// <summary>Resolve a module-relative path. A leading / means relative to src/.</summary>
	public static string Resolve( string dir, string path )
	{
		path = path.Replace( '\\', '/' );
		if ( path.StartsWith( '/' ) ) return path[1..];
		if ( path.StartsWith( "../" ) || Path.IsPathRooted( path ) ) return ToSrcRelative( Path.GetFullPath( Path.Combine( SrcDir, dir, path ) ) );
		return string.IsNullOrEmpty( dir ) ? path : $"{dir}/{path}";
	}

	/// <summary>Path from a module directory to a src-relative file, as written into the .vcxproj.</summary>
	public static string Relative( string fromDir, string srcRelative )
	{
		var from = Path.Combine( SrcDir, fromDir );
		var to = Path.Combine( SrcDir, srcRelative );
		return Path.GetRelativePath( from, to ).Replace( '/', '\\' );
	}

	private static Dictionary<string, (string Name, string Dir)> modules;

	/// <summary>
	/// Module name and directory, from the src/**/&lt;name&gt;.build.cs that declares it. Matched to the
	/// class name ignoring case and underscores, so filesystem_stdio.build.cs declares FilesystemStdio.
	/// </summary>
	public static (string Name, string Dir) Locate( string typeName )
	{
		modules ??= Directory.EnumerateFiles( SrcDir, "*.build.cs", SearchOption.AllDirectories )
			.ToDictionary(
				f => Key( Path.GetFileName( f )[..^".build.cs".Length] ),
				f => (Path.GetFileName( f )[..^".build.cs".Length].ToLowerInvariant(), ToSrcRelative( Path.GetDirectoryName( f ) )) );

		return modules.TryGetValue( Key( typeName ), out var module )
			? module
			: throw new FileNotFoundException( $"No src/**/*.build.cs declares '{typeName}'." );
	}

	private static string Key( string name ) => name.Replace( "_", "" ).ToLowerInvariant();

	public static IEnumerable<string> Glob( string dir, string glob )
	{
		var full = Resolve( dir, glob );
		var root = full.Contains( '/' ) ? full[..full.LastIndexOf( '/' )] : "";
		var pattern = full[(root.Length == 0 ? 0 : root.Length + 1)..];
		var recursive = root.EndsWith( "**" );
		if ( recursive ) root = root[..^2].TrimEnd( '/' );

		var absolute = Path.Combine( SrcDir, root );
		if ( !Directory.Exists( absolute ) ) return [];

		var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		return Directory.EnumerateFiles( absolute, pattern, option )
			.Select( ToSrcRelative )
			.OrderBy( x => x, StringComparer.OrdinalIgnoreCase );
	}

	public static Func<string, bool> Matcher( string glob )
	{
		var pattern = "^" + Regex.Escape( glob.Replace( '\\', '/' ) )
			.Replace( @"\*\*/", ".*" )
			.Replace( @"\*", "[^/]*" )
			.Replace( @"\?", "." ) + "$";
		var regex = new Regex( pattern, RegexOptions.IgnoreCase );
		return path => regex.IsMatch( path );
	}

	private static string FindSrcDir()
	{
		var dir = Directory.GetCurrentDirectory();
		while ( dir is not null )
		{
			var candidate = Path.Combine( dir, "src" );
			if ( Directory.Exists( Path.Combine( candidate, "engine2" ) ) )
				return candidate;
			if ( Directory.Exists( Path.Combine( dir, "engine2" ) ) ) return dir;
			dir = Path.GetDirectoryName( dir );
		}

		throw new DirectoryNotFoundException( "Could not locate src/ from the current directory." );
	}
}
