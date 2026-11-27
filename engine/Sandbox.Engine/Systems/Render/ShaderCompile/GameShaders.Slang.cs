using System.IO;
using System.Threading;
using Borealis.Backend;
using Sandbox.ShaderCompiler;

namespace Sandbox.Engine.Shaders;

/// <summary>
/// Slang source given to <see cref="Shader.CompileAsync(string, string, CancellationToken)"/> and its overloads (a surface, a
/// raw module or a pass module), told from vfx's <c>.shader</c> text by its content. It's written into the cache folder as
/// <c>compiled/name.slang</c> and compiled offline there, as the editor compiles a project's (<see cref="OfflineShaders"/>),
/// so a compile error throws here, and Borealis.Gpu, which finds it under that name, reads the compile instead of compiling
/// it again. Its materials name it <c>compiled/name.shader</c>, as a vfx shader compiled here.
/// </summary>
internal static partial class GameShaders
{
	/// <summary>
	/// Whether <paramref name="source"/> is Slang rather than vfx's <c>.shader</c>, told by content as everywhere else
	/// (<see cref="ShaderFormat.IsVfxSource"/>): anything that isn't a VFX source. Blank text is neither, and goes to vfx.
	/// </summary>
	internal static bool IsSlang( string source ) =>
		!string.IsNullOrWhiteSpace( source ) && !ShaderFormat.IsVfxSource( source );

	static readonly HashSet<string> slangLinks = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The links <see cref="CompileSlang"/> made onto GAME go, as the game closes.</summary>
	static void RemoveSlangLinks()
	{
		foreach ( var link in slangLinks ) NativeEngine.FullFileSystem.RemoveSymLink( link, "GAME" );
		slangLinks.Clear();
	}

	/// <summary>The folder the cache's Slang sources are found in by name (<c>compiled/name.slang</c>), for Borealis.Gpu's content.</summary>
	internal static string SlangFolder => CacheFolder;

	/// <summary>
	/// Compile Slang <paramref name="source"/> as <paramref name="relativePath"/> (<c>effects/water.shader</c>), with
	/// <paramref name="siblings"/> (files beside it it imports, by path relative to its folder) written beside it.
	/// </summary>
	static async Task<Shader> CompileSlang( string relativePath, string source, IReadOnlyList<(string Name, byte[] Data)> siblings, CancellationToken token )
	{
		ThreadSafe.AssertIsMainThread();
		var stem = relativePath[..^Path.GetExtension( relativePath ).Length];
		var resourceName = (ResourcePrefix + stem + ".shader").ToLowerInvariant();
		var slangPath = Path.Combine( CacheFolder, (ResourcePrefix + stem).ToLowerInvariant() + ".slang" );

		var compiled = await Gated( token, async cancel =>
		{
			// The same name compiled from vfx text before: its files and link go, so the Slang module is what the name finds
			var folder = Path.GetDirectoryName( slangPath );
			Directory.CreateDirectory( folder );
			var vfxPath = Path.Combine( CacheFolder, resourceName );
			File.Delete( vfxPath );
			File.Delete( vfxPath + "_c" );
			if ( links.Remove( resourceName ) ) NativeEngine.FullFileSystem.RemoveSymLink( resourceName + "_c", "GAME" );
			foreach ( var (name, data) in siblings )
			{
				var file = Path.Combine( folder, name );
				Directory.CreateDirectory( Path.GetDirectoryName( file ) );
				if ( !File.Exists( file ) || !File.ReadAllBytes( file ).AsSpan().SequenceEqual( data ) ) await File.WriteAllBytesAsync( file, data, cancel );
			}

			// Unchanged text isn't written again: its compile beside it stays current
			if ( !File.Exists( slangPath ) || await File.ReadAllTextAsync( slangPath, cancel ) != source ) await File.WriteAllTextAsync( slangPath, source, cancel );

			var errors = new List<string>();
			var game = Path.GetFullPath( EngineFileSystem.Root.GetFullPath( "/" ) );
			OfflineResult result;
			try
			{
				result = await Task.Run( () => OfflineShaders.CompileGameShaderAsync( game, slangPath, message => { lock ( errors ) errors.Add( message.Replace( slangPath, relativePath ) ); } ), cancel );
			}
			catch ( Exception e ) when ( e is not OperationCanceledException )
			{
				throw new ShaderCompileException( relativePath, [e.Message] );
			}

			if ( result == OfflineResult.Skipped ) throw new ShaderCompileException( relativePath, ["Slang source that declares no [Material] surface or raw module, and doesn't include sbox/pass.slangh"] );
			if ( result == OfflineResult.Failed ) throw new ShaderCompileException( relativePath, errors.Count > 0 ? errors : ["Didn't compile"] );
			return true;
		} );

		if ( !compiled || !Graphics.IsAvailable ) return null;

		// On GAME too, so native's material system takes it for a shader only a module describes, not a missing one
		var slangResource = resourceName[..^".shader".Length] + ".slang";
		if ( slangLinks.Add( slangResource ) ) NativeEngine.FullFileSystem.AddSymLink( slangResource, "GAME", slangPath );
		return Shader.Load( resourceName );
	}

	/// <summary>The Slang files beside <paramref name="relativePath"/> in <paramref name="fileSystem"/>, and under its folder, for its imports.</summary>
	static List<(string Name, byte[] Data)> SlangSiblings( BaseFileSystem fileSystem, string relativePath )
	{
		var folder = (Path.GetDirectoryName( relativePath ) ?? "").Replace( '\\', '/' );
		var siblings = new List<(string, byte[])>();
		foreach ( var file in fileSystem.FindFile( folder, "*", recursive: true ) )
		{
			var name = file.Replace( '\\', '/' );
			var path = folder.Length > 0 ? $"{folder}/{name}" : name;
			if ( string.Equals( path, relativePath, StringComparison.OrdinalIgnoreCase ) ) continue;
			// A .shader beside it is Slang or vfx, told by content: only a Slang one can be imported
			var shader = name.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase );
			if ( !shader && !name.EndsWith( ".slang", StringComparison.OrdinalIgnoreCase ) && !name.EndsWith( ".slangh", StringComparison.OrdinalIgnoreCase ) ) continue;
			var data = fileSystem.ReadAllBytes( path ).ToArray();
			if ( shader && !IsSlang( System.Text.Encoding.UTF8.GetString( data ) ) ) continue;
			siblings.Add( (name, data) );
		}

		return siblings;
	}
}
