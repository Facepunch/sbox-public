using System.IO;
using System.Linq;
using System.Threading;

namespace Borealis.Source2;

/// <summary>
/// Where compiled content is found: folders searched in order for a path like <c>shaders/complex.shader</c>, which
/// resolves to its compiled form (<c>.shader_c</c>), as the engine's GAME search path does.
/// </summary>
public sealed class ContentFiles
{
	// Each list is replaced whole when added to (under gate), never changed in place: the read workers search them without
	// a lock while the main thread adds a map's folder or package
	readonly object gate = new();
	string[] roots = [];

	static void Append<T>( ref T[] list, T item, object gate )
	{
		lock ( gate ) Volatile.Write( ref list, [.. list, item] );
	}

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
		if ( Directory.Exists( root ) ) Append( ref roots, Path.GetFullPath( root ), gate );
	}

	string[] fallbacks = [];

	/// <summary>
	/// Folders searched last, after <see cref="Resolve"/>: the rest of the engine's GAME search path, which a game's files
	/// come before.
	/// </summary>
	public IReadOnlyList<string> Fallbacks => fallbacks;

	/// <summary>Search another folder last, after the others and <see cref="Resolve"/>.</summary>
	public void AddFallback( string root )
	{
		if ( Directory.Exists( root ) ) Append( ref fallbacks, Path.GetFullPath( root ), gate );
	}

	/// <summary>
	/// The game folder above <paramref name="start"/> (a repo checkout's <c>game/</c>): its <c>core</c> folder, then the
	/// citizen addon's assets when they're there, as the engine mounts them into its core content; empty if none is found.
	/// The rest of the engine's GAME search path are <see cref="Fallbacks"/>: the content addon's assets, then the menu's
	/// assets and the files compiled for them (<c>addons/menu/Transients</c>, which has the <c>sbox_props</c> models the menu
	/// uses, such as tall grass).
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
				var files = new ContentFiles( core, Path.Combine( game, "addons", "citizen", "Assets" ) );
				files.AddFallback( Path.Combine( game, "addons", "content", "Assets" ) );
				files.AddFallback( Path.Combine( game, "addons", "menu", "Assets" ) );
				files.AddFallback( Path.Combine( game, "addons", "menu", "Transients" ) );
				return files;
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

		if ( Resolve?.Invoke( path ) is { } resolved && File.Exists( resolved ) ) return MostOf( resolved );

		foreach ( var root in fallbacks )
		{
			var full = Path.Combine( root, path );
			if ( File.Exists( full ) ) return full;
		}

		return null;
	}

	/// <summary>
	/// A web-streamed texture's file on disk is the start of it (its smaller mips); the engine keeps the start of the whole
	/// file it has downloaded so far beside it (<c>.resident</c>), finer mips and all: that, when it holds more.
	/// </summary>
	static string MostOf( string file )
	{
		if ( !file.EndsWith( ".vtex_c", StringComparison.OrdinalIgnoreCase ) ) return file;
		var resident = new FileInfo( file + ".resident" );
		return resident.Exists && resident.Length > new FileInfo( file ).Length ? file + ".resident" : file;
	}

	/// <summary>A compiled resource's bytes; throws if it isn't anywhere.</summary>
	public byte[] Read( string path ) => TryRead( path ) ?? throw new FileNotFoundException( $"No compiled {path} under {string.Join( ", ", roots.Concat( packages.Select( x => x.ToString() ) ) )}" );

	Resources.VpkFile[] packages = [];

	/// <summary>
	/// Also search a VPK (a compiled map's), after the folders. Files in folders win, as the engine's search path puts
	/// loose files first.
	/// </summary>
	public void AddPackage( Resources.VpkFile package ) => Append( ref packages, package, gate );

	/// <summary>The VPKs searched after the folders.</summary>
	public IReadOnlyList<Resources.VpkFile> Packages => packages;

	/// <summary>A compiled resource's bytes from the folders or packages (<c>_c</c> appended unless it's there), or null.</summary>
	public byte[] TryRead( string path )
	{
		if ( Find( path ) is { } file ) return File.ReadAllBytes( file );
		var packages = this.packages;
		if ( packages.Length == 0 || string.IsNullOrEmpty( path ) ) return null;

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

		var packages = this.packages;
		if ( packages.Length == 0 || string.IsNullOrEmpty( path ) ) return null;

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

		var packages = this.packages;
		if ( packages.Length == 0 || string.IsNullOrEmpty( path ) ) return null;

		path = path.Replace( '\\', '/' ).TrimStart( '/' );
		if ( !path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) path += "_c";
		foreach ( var package in packages )
		{
			if ( package.ReadPrefixPooled( path, most, out length ) is { } data ) return data;
		}

		return null;
	}
}
