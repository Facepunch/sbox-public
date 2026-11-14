using System.IO;
using System.Linq;

namespace Borealis.Source2;

/// <summary>
/// Where compiled content is found: folders searched in order for a path like <c>shaders/complex.shader</c>, which
/// resolves to its compiled form (<c>.shader_c</c>), as the engine's GAME search path does.
/// </summary>
public sealed class ContentFiles
{
	readonly List<string> roots = new();

	/// <summary>Search these folders, first first.</summary>
	public ContentFiles( params string[] roots )
	{
		foreach ( var root in roots ) Add( root );
	}

	/// <summary>The folders searched.</summary>
	public IReadOnlyList<string> Roots => roots;

	/// <summary>Search another folder, after the others.</summary>
	public void Add( string root )
	{
		if ( Directory.Exists( root ) ) roots.Add( Path.GetFullPath( root ) );
	}

	/// <summary>
	/// The game folder above <paramref name="start"/> (a repo checkout's <c>game/</c>): its <c>core</c> folder, then the
	/// citizen addon's assets when they're there, as the engine mounts them into its core content; empty if none is found.
	/// </summary>
	public static ContentFiles FindGame( string start = null )
	{
		var directory = new DirectoryInfo( start ?? AppContext.BaseDirectory );
		while ( directory is not null )
		{
			foreach ( var game in (string[])[Path.Combine( directory.FullName, "game" ), directory.FullName] )
			{
				var core = Path.Combine( game, "core" );
				if ( !Directory.Exists( Path.Combine( core, "shaders" ) ) ) continue;
				return new ContentFiles( core, Path.Combine( game, "addons", "citizen", "Assets" ) );
			}

			directory = directory.Parent;
		}

		return new ContentFiles();
	}

	/// <summary>
	/// The full path of a resource's compiled file (<c>_c</c> appended unless it's there), or null.
	/// </summary>
	public string Find( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return null;
		path = path.Replace( '\\', '/' ).TrimStart( '/' );
		if ( !path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) path += "_c";

		foreach ( var root in roots )
		{
			var full = Path.Combine( root, path );
			if ( File.Exists( full ) ) return full;
		}

		return null;
	}

	/// <summary>A compiled resource's bytes; throws if it isn't anywhere.</summary>
	public byte[] Read( string path ) => TryRead( path ) ?? throw new FileNotFoundException( $"No compiled {path} under {string.Join( ", ", roots.Concat( packages.Select( x => x.ToString() ) ) )}" );

	readonly List<Resources.VpkFile> packages = new();

	/// <summary>
	/// Also search a VPK (a compiled map's), after the folders. Files in folders win, as the engine's search path puts
	/// loose files first.
	/// </summary>
	public void AddPackage( Resources.VpkFile package ) => packages.Add( package );

	/// <summary>The VPKs searched after the folders.</summary>
	public IReadOnlyList<Resources.VpkFile> Packages => packages;

	/// <summary>A compiled resource's bytes from the folders or packages (<c>_c</c> appended unless it's there), or null.</summary>
	public byte[] TryRead( string path )
	{
		if ( Find( path ) is { } file ) return File.ReadAllBytes( file );
		if ( packages.Count == 0 || string.IsNullOrEmpty( path ) ) return null;

		path = path.Replace( '\\', '/' ).TrimStart( '/' );
		if ( !path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) path += "_c";
		foreach ( var package in packages )
		{
			if ( package.Read( path ) is { } data ) return data;
		}

		return null;
	}
}
