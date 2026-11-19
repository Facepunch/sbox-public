using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borealis.Source2;
using Sandbox.ShaderCompiler;

namespace Borealis.Backend;

/// <summary>
/// The engine's Slang shaders compiled offline (<see cref="SlangOutput"/>, <c>name.slang_c</c>): every surface, raw module and
/// pass module under the content roots' <c>shaders</c> folders, and the surfaces converted from core <c>.shader</c> files
/// (<c>bark.slang_c</c> in the core shaders). Made by build-shaders through <c>ShaderCompiler -slang</c> and shipped as
/// <c>.shader_c</c> files are, so Borealis.Gpu runs Slang in game only for hotload, game code's compiles and package shaders.
/// See docs/reference/borealis-slang-materials.md.
/// </summary>
public static class OfflineShaders
{
	/// <summary>
	/// Compile every Slang shader of the game folder <paramref name="game"/> whose offline compile is missing or out of date,
	/// with every core. Returns how many sources didn't fully compile (0 on success); progress and errors go to
	/// <paramref name="log"/>.
	/// </summary>
	public static int CompileAll( string game, Action<string> log )
	{
		var clock = Stopwatch.StartNew();
		var shaders = Path.Combine( game, "core", "shaders" );
		if ( !File.Exists( Path.Combine( shaders, "sbox", "surface.slang" ) ) )
		{
			log?.Invoke( $"No Slang surface library in {shaders}" );
			return 1;
		}

		SlangCompiler.CompileThreads = Environment.ProcessorCount;
		var roots = ContentFiles.FindGame().Roots;
		var library = Path.Combine( shaders, "sbox" ) + Path.DirectorySeparatorChar;
		var sources = roots.Select( r => Path.Combine( r, "shaders" ) ).Prepend( shaders ).Distinct( StringComparer.OrdinalIgnoreCase ).Where( Directory.Exists )
			.SelectMany( f => Directory.GetFiles( f, "*.slang", SearchOption.AllDirectories ) )
			.Where( f => !Path.GetFullPath( f ).StartsWith( library, StringComparison.OrdinalIgnoreCase ) )
			.Select( f => (Source: f, Output: (string)null) )
			.ToList();

		// The surfaces converted from core .shader files at run time: compiled from the same text, kept in the core shaders
		var converted = Path.Combine( game, ".source2", "borealisoffline", "converted" );
		foreach ( var file in SurfaceShaders.DefaultSurfaceFiles( shaders, roots, converted, log ) )
		{
			if ( Path.GetFullPath( file ).StartsWith( Path.GetFullPath( converted ), StringComparison.OrdinalIgnoreCase ) )
				sources.Add( (file, SlangOutput.PathOf( Path.Combine( shaders, Path.GetFileName( file ) ) )) );
		}

		var compiler = new SlangCompiler( EngineFolders.NativeIn( game ), [shaders] ) { CacheFolder = Path.Combine( game, ".source2", "borealisoffline" ), UseOffline = false };
		var results = Task.WhenAll( sources.Select( s => Task.Run( async () =>
		{
			var result = await compiler.CompileOfflineAsync( s.Source, s.Output, log ).ConfigureAwait( false );
			if ( result is OfflineResult.Compiled or OfflineResult.Failed ) log?.Invoke( $"{Path.GetFileName( s.Source )}: {result}" );
			return result;
		} ) ) ).GetAwaiter().GetResult();

		var failed = results.Count( r => r == OfflineResult.Failed );
		log?.Invoke( $"Slang offline: {results.Count( r => r == OfflineResult.Compiled )} compiled, {results.Count( r => r == OfflineResult.UpToDate )} up to date, {failed} failed, {results.Count( r => r == OfflineResult.Skipped )} not shaders, in {clock.Elapsed.TotalSeconds:F0} s" );
		return failed;
	}
}
