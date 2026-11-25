using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// Compiled shaders: a source's offline compile (<see cref="SlangOutput"/>) first, then the disk cache, one file per source
/// file (<see cref="ShaderCacheFile"/>). An entry is found by a key of the compiler's identity, the tier and the source's file name and
/// text, and is used only while every file the compile read still has the contents it had then; anything else compiles
/// again and replaces it. Keys and the files read are relative (<see cref="DependencyPaths"/>), so the build's offline compile
/// serves any install.
/// </summary>
internal static class CompileCache
{
	/// <summary>Raised with any change to how an entry is keyed.</summary>
	const int Format = 3;

	static readonly Dictionary<string, string> identities = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The compiler's identity: <see cref="SlangCompiler.Version"/> (what this assembly makes of a source), the Slang it
	/// drives (its loader and compiler libraries' contents) and the optimizer after it. Not this assembly's build, so an
	/// engine build, or a Debug build beside a Release one, keeps every entry whose inputs didn't change.
	/// </summary>
	public static string Identity( string slangDirectory )
	{
		lock ( identities )
		{
			if ( identities.TryGetValue( slangDirectory, out var known ) ) return known;
			var libraries = OperatingSystem.IsWindows() ? new[] { "slang.dll", "slang-compiler.dll" } : ["libslang.so", "libslang-compiler.so"];
			var hashes = libraries.Select( l => Path.Combine( slangDirectory, l ) ).Select( l => File.Exists( l ) ? Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( l ) ) )[..16] : "none" );
			return identities[slangDirectory] = $"{SlangCompiler.Version}:{string.Join( ':', hashes )}:{SpirvOptimizer.Identity( slangDirectory )}";
		}
	}

	internal static string Hash( string text ) => Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( text ) ) );

	/// <summary>An entry's key: the compiler, the tier, the file's name and its text; whether it's the layout entry.</summary>
	public static string Key( string slangDirectory, ShaderTier tier, string path, string text, bool layout ) =>
		Hash( $"{Format}|{Identity( slangDirectory )}|{tier}|{Path.GetFileName( path ).ToLowerInvariant()}|{layout}|{text}" );

	/// <summary>The file an entry for <paramref name="path"/> lives in, and the prefix every entry for that file shares.</summary>
	static (string File, string Prefix) Place( string folder, string path, string key, bool layout )
	{
		var prefix = $"{Path.GetFileNameWithoutExtension( path )}.{(layout ? "layout." : "")}{Hash( Path.GetFullPath( path ).ToLowerInvariant() )[..8].ToLowerInvariant()}.";
		return (Path.Combine( folder, prefix + key[..16].ToLowerInvariant() + ".bin" ), prefix);
	}

	/// <summary>
	/// The compiled <paramref name="path"/> from its offline compile (<paramref name="offline"/>), else from <paramref name="folder"/>,
	/// or null. With <paramref name="layout"/>, its small entry of the name and material layout alone (no passes), which every
	/// compile writes too.
	/// </summary>
	public static CompiledShader Load( string folder, IReadOnlyList<SlangOutput> offline, DependencyPaths paths, string slangDirectory, ShaderTier tier, string path, string text, bool layout = false )
	{
		var key = Key( slangDirectory, tier, path, text, layout );
		CompiledShader Read( ReadOnlySpan<byte> bytes, bool fromOffline ) => ShaderCacheFile.TryRead( bytes, fromOffline ? "" : key, paths.Resolve, ( r, dependencies ) => ShaderBinary.ReadShader( r, path, dependencies, fromOffline ), out var shader ) ? shader : null;

		foreach ( var output in offline )
		{
			if ( output.TryGet( key, out var bytes ) && Read( bytes.Span, true ) is { } compiled ) return compiled;
		}

		if ( folder is not null )
		{
			var (file, _) = Place( folder, path, key, layout );
			try
			{
				if ( File.Exists( file ) && Read( File.ReadAllBytes( file ), false ) is { } cached ) return cached;
			}
			catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
			{
			}
		}

		return null;
	}

	/// <summary>
	/// An entry for <paramref name="shader"/>, compiled from <paramref name="text"/>: its key, bytes and the files it read as
	/// named there, or null when it read a file <paramref name="paths"/> can't name.
	/// </summary>
	public static (string Key, byte[] Bytes, List<(string Name, string Hash)> Dependencies)? Entry( DependencyPaths paths, string slangDirectory, string path, string text, CompiledShader shader, bool layout )
	{
		var dependencies = new List<(string, string)>();
		foreach ( var dependency in shader.Dependencies )
		{
			if ( paths.Name( dependency ) is not { } name ) return null;
			dependencies.Add( (name, PassCache.FileHash( dependency )) );
		}

		var key = Key( slangDirectory, shader.Tier, path, text, layout );
		var saved = layout ? new CompiledShader { Name = shader.Name, Tier = shader.Tier, Material = shader.Material, Passes = new Dictionary<string, CompiledPass>() } : shader;
		return (key, ShaderCacheFile.Write( key, dependencies, w => ShaderBinary.WriteShader( w, saved ) ), dependencies);
	}

	/// <summary>
	/// Keep <paramref name="shader"/>, compiled from <paramref name="text"/>, replacing older entries for its file, and its
	/// layout entry (<see cref="Load"/>); with <paramref name="layout"/>, only that. <paramref name="saved"/> is told of each.
	/// </summary>
	public static void Save( string folder, DependencyPaths paths, string slangDirectory, string path, string text, CompiledShader shader, bool layout = false, Action<string, byte[], List<(string Name, string Hash)>> saved = null )
	{
		if ( !layout ) Save( folder, paths, slangDirectory, path, text, shader, true, saved );
		if ( Entry( paths, slangDirectory, path, text, shader, layout ) is not var (key, bytes, dependencies) ) return;
		saved?.Invoke( key, bytes, dependencies );
		if ( folder is null ) return;

		var (file, prefix) = Place( folder, path, key, layout );
		ShaderCacheFile.Save( file, bytes );
		try
		{
			foreach ( var old in Directory.GetFiles( folder, prefix + "*.bin" ) )
			{
				if ( !string.Equals( old, file, StringComparison.OrdinalIgnoreCase ) ) File.Delete( old );
			}
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
		}
	}
}

/// <summary>
/// The files a compile read, named in a cache entry relative to where the compiler finds them: <c>&lt;0&gt;/sbox/surface.slang</c>
/// under its first search path, <c>&lt;m&gt;/rock.slang</c> beside the module. A file elsewhere has no name, and its entry
/// isn't kept.
/// </summary>
internal sealed class DependencyPaths( IReadOnlyList<string> searchPaths, string moduleDirectory )
{
	public string Name( string path )
	{
		var full = Path.GetFullPath( path );
		for ( int i = 0; i < searchPaths.Count; i++ )
		{
			if ( Under( full, searchPaths[i] ) is { } relative ) return $"<{i}>/{relative}";
		}

		return Under( full, moduleDirectory ) is { } local ? $"<m>/{local}" : null;
	}

	public string Resolve( string name )
	{
		if ( name.StartsWith( "<m>/", StringComparison.Ordinal ) ) return Path.Combine( moduleDirectory, name[4..] );
		var end = name.IndexOf( ">/", StringComparison.Ordinal );
		if ( !name.StartsWith( '<' ) || end < 0 || !int.TryParse( name.AsSpan( 1, end - 1 ), out var index ) || index >= searchPaths.Count ) return null;
		return Path.Combine( searchPaths[index], name[(end + 2)..] );
	}

	static string Under( string full, string folder )
	{
		if ( string.IsNullOrEmpty( folder ) ) return null;
		var root = Path.GetFullPath( folder ).TrimEnd( '\\', '/' ) + Path.DirectorySeparatorChar;
		return full.StartsWith( root, StringComparison.OrdinalIgnoreCase ) ? full[root.Length..].Replace( '\\', '/' ) : null;
	}
}
