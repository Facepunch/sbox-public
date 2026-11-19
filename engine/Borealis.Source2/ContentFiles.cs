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
	/// Asked after the folders: where the host's own file system has a compiled file on disk, or null. A game package's
	/// files are named by their hash on disk, so only the host knows where its paths are.
	/// </summary>
	public Func<string, string> Resolve { get; set; }

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

		return Resolve?.Invoke( path ) is { } resolved && File.Exists( resolved ) ? resolved : null;
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

	/// <summary>
	/// The first <paramref name="most"/> bytes of a compiled resource (all of it when it's shorter) from the folders or
	/// packages, as <see cref="TryRead"/> finds it, or null.
	/// </summary>
	public byte[] TryReadPrefix( string path, long most )
	{
		if ( Find( path ) is { } file )
		{
			using var stream = new FileStream( file, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan );
			var data = new byte[(int)Math.Min( stream.Length, Math.Max( most, 0 ) )];
			stream.ReadExactly( data );
			return data;
		}

		if ( packages.Count == 0 || string.IsNullOrEmpty( path ) ) return null;

		path = path.Replace( '\\', '/' ).TrimStart( '/' );
		if ( !path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) path += "_c";
		foreach ( var package in packages )
		{
			if ( package.ReadPrefix( path, most ) is { } data ) return data;
		}

		return null;
	}

	/// <summary>
	/// As <see cref="TryReadPrefix"/>, into a <see cref="ContentBuffers"/> array whose first <paramref name="length"/>
	/// bytes are the file's (give it back with <see cref="ContentBuffers.Return"/>), or null: for streaming reads, which
	/// mustn't make garbage. <see cref="long.MaxValue"/> reads the whole file.
	/// </summary>
	public byte[] TryReadPrefixPooled( string path, long most, out int length )
	{
		length = 0;
		if ( Find( path ) is { } file )
		{
			using var handle = File.OpenHandle( file, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan );
			length = (int)Math.Min( RandomAccess.GetLength( handle ), Math.Max( most, 0 ) );
			var data = ContentBuffers.Rent( length );
			try
			{
				for ( int read = 0; read < length; )
				{
					var got = RandomAccess.Read( handle, data.AsSpan( read, length - read ), read );
					if ( got <= 0 ) throw new EndOfStreamException( $"{file} ended while it was read" );
					read += got;
				}
			}
			catch
			{
				ContentBuffers.Return( data );
				length = 0;
				throw;
			}

			return data;
		}

		if ( packages.Count == 0 || string.IsNullOrEmpty( path ) ) return null;

		path = path.Replace( '\\', '/' ).TrimStart( '/' );
		if ( !path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) path += "_c";
		foreach ( var package in packages )
		{
			if ( package.ReadPrefixPooled( path, most, out length ) is { } data ) return data;
		}

		return null;
	}
}
