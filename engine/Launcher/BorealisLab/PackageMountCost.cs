using Borealis.Backend;
using Borealis.Source2;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sandbox.BorealisLab;

/// <summary>
/// <c>-package-mount-cost folder -package-roots "a;b" [-vertex-waves 0]</c>: what compiling a package's shaders on mount costs
/// (<see cref="PassModules.CompileMounted"/>), per root: every outdated <c>.shader_c</c> under it made into its embedded module as
/// the game makes it, and each module's first stages (<see cref="PassModules.FirstStages"/>) compiled on the compiler's threads,
/// first with an empty cache (a player's first load), then again from the cache (every load after). Writes
/// <c>mount-cost.tsv</c> into the folder. No device and nothing of the engine starts.
/// </summary>
internal static class PackageMountCost
{
	public static bool Requested => Args.String( "-package-mount-cost" ) is not null;

	public static int Run()
	{
		var folder = Path.GetFullPath( Args.String( "-package-mount-cost" ) );
		Directory.CreateDirectory( folder );
		var core = ContentFiles.FindGame().Roots.First( r => File.Exists( Path.Combine( r, "shaders", "sbox", "pass.slangh" ) ) );
		var shaders = Path.Combine( core, "shaders" );
		var native = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );
		var vertexWaves = Args.Int( "-vertex-waves", 1 ) != 0;

		var lines = new List<string> { "root\tfiles\tmodules\tstages\tfailed\tcold s\twarm s" };
		foreach ( var root in (Args.String( "-package-roots" ) ?? "").Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			var files = Directory.EnumerateFiles( root, "*.shader_c", SearchOption.AllDirectories ).Order( StringComparer.OrdinalIgnoreCase ).ToList();
			var modules = new List<(PassModule Module, ShaderFile Shader)>();
			var embedded = Path.Combine( folder, "modules" );
			Directory.CreateDirectory( embedded );
			foreach ( var file in files )
			{
				ShaderFile shader;
				try
				{
					shader = ShaderFile.LoadOutdated( file );
				}
				catch ( Exception e ) when ( e is InvalidDataException or IOException or EndOfStreamException )
				{
					continue;
				}

				if ( shader.Outdated is null || shader.EmbeddedSource is not { } source || PassConverter.RefuseEmbedded( source ) is not null ) continue;
				var stem = Regex.Replace( Path.GetFileName( file ).Split( '.' )[0], "[^a-zA-Z0-9_]", "_" );
				var text = PassConverter.FromEmbedded( source, (shader.Features?.StaticCombos ?? []).Select( c => (c.Name, c.Min, c.Max) ), stem );
				var path = Path.Combine( embedded, $"{stem}.{System.Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( text ) ), 0, 8 ).ToLowerInvariant()}.slang" );
				File.WriteAllText( path, text );
				modules.Add( (PassModule.FromText( path, text, [shaders] ), shader) );
			}

			var cache = Path.Combine( folder, "cache", Path.GetFileName( root.TrimEnd( '/', '\\' ) ) );
			if ( Directory.Exists( cache ) ) Directory.Delete( cache, true );
			var (stages, failed, cold) = Compile( native, shaders, cache, modules, vertexWaves );
			var (_, _, warm) = Compile( native, shaders, cache, modules, vertexWaves );
			var line = $"{root}\t{files.Count}\t{modules.Count}\t{stages}\t{failed}\t{cold.TotalSeconds:F1}\t{warm.TotalSeconds:F2}";
			Console.WriteLine( line );
			lines.Add( line );
		}

		File.WriteAllLines( Path.Combine( folder, "mount-cost.tsv" ), lines );
		return 0;
	}

	/// <summary>Every module's first stages at once on a new compiler with <paramref name="cache"/>; the stages, the failures and the wall time.</summary>
	static (int Stages, int Failed, TimeSpan Elapsed) Compile( string native, string shaders, string cache, List<(PassModule Module, ShaderFile Shader)> modules, bool vertexWaves )
	{
		var compiler = new SlangCompiler( native, [shaders] ) { CacheFolder = cache };
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		var tasks = modules.SelectMany( m => PassModules.FirstStages( m.Module, m.Shader, vertexWaves ).Select( s => compiler.CompilePassAsync( m.Module, s.Kind, s.Values ) ) ).ToList();
		var failed = 0;
		foreach ( var task in tasks )
		{
			try
			{
				task.GetAwaiter().GetResult();
			}
			catch ( ShaderCompileException )
			{
				failed++;
			}
		}

		return (tasks.Count, failed, System.Diagnostics.Stopwatch.GetElapsedTime( start ));
	}
}
