using System.IO;

namespace Basalt.Source2;

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
	/// The game folder above <paramref name="start"/> (a repo checkout's <c>game/</c>), with its <c>core</c> folder; empty if none is found.
	/// </summary>
	public static ContentFiles FindGame( string start = null )
	{
		var directory = new DirectoryInfo( start ?? AppContext.BaseDirectory );
		while ( directory is not null )
		{
			var core = Path.Combine( directory.FullName, "game", "core" );
			if ( Directory.Exists( Path.Combine( core, "shaders" ) ) ) return new ContentFiles( core );
			core = Path.Combine( directory.FullName, "core" );
			if ( Directory.Exists( Path.Combine( core, "shaders" ) ) ) return new ContentFiles( core );
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
	public byte[] Read( string path ) => File.ReadAllBytes( Find( path ) ?? throw new FileNotFoundException( $"No compiled {path} under {string.Join( ", ", roots )}" ) );
}
