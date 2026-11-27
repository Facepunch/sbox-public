using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borealis.Source2;
using Sandbox.ShaderCompiler;

namespace Borealis.Backend;

/// <summary>
/// The engine's Slang shaders compiled offline (<see cref="SlangOutput"/>, <c>name.slang_c</c>): every surface, raw module and
/// pass module under the content roots' <c>shaders</c> folders and the repo's addons' assets, and the surfaces converted
/// from core <c>.shader</c> files (<c>bark.slang_c</c> in the core shaders). Made by build-shaders through
/// <c>ShaderCompiler -slang</c> and shipped as <c>.shader_c</c> files are, so Borealis.Gpu runs Slang in game only for
/// hotload, game code's compiles, package shaders without a current offline compile, and what has none.
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
		if ( UpdateRecords( game, log ) != 0 ) return 1;
		var sources = Sources( game, log );
		var compiler = new SlangCompiler( EngineFolders.NativeIn( game ), [shaders] ) { CacheFolder = Path.Combine( game, ".source2", "borealisoffline" ), UseOffline = false, Warning = log };
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

	static readonly Dictionary<string, SlangCompiler> gameCompilers = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Told the compiler's warnings (<see cref="SlangCompiler.Warning"/>: a deprecated API level's) for a game project's Slang; the editor's console.</summary>
	public static Action<string> GameWarning { get; set; }

	/// <summary>
	/// The compiler a game project's Slang compiles offline with, for the game folder <paramref name="game"/>: the one the
	/// game's device compiles with at run time (its Slang, the core shaders as its search path), so the entries it writes
	/// are the ones a player's device looks for. Null when the game folder has no surface library.
	/// </summary>
	static SlangCompiler GameCompiler( string game )
	{
		var shaders = Path.Combine( game, "core", "shaders" );
		if ( !File.Exists( Path.Combine( shaders, "sbox", "surface.slang" ) ) ) return null;
		lock ( gameCompilers )
		{
			if ( gameCompilers.TryGetValue( game, out var known ) ) return known;
			return gameCompilers[game] = new SlangCompiler( EngineFolders.NativeIn( game ), [shaders] ) { CacheFolder = Path.Combine( game, ".source2", "borealisoffline" ), UseOffline = false, Warning = message => GameWarning?.Invoke( message ) };
		}
	}

	/// <summary>
	/// Compile a game project's Slang shader <paramref name="source"/> (a surface, raw module or pass module, by its full
	/// path) offline into <c>name.slang_c</c> beside it (<see cref="SlangOutput"/>), as build-shaders does the engine's, so
	/// players read it instead of compiling it: the editor does on save and at publish. Nothing is compiled when the output
	/// is current; a module others import (one with no <c>[Material]</c> or declarations) is skipped. Errors go to
	/// <paramref name="log"/>.
	/// </summary>
	public static Task<OfflineResult> CompileGameShaderAsync( string game, string source, Action<string> log )
	{
		if ( GameCompiler( game ) is not { } compiler )
		{
			log?.Invoke( $"No Slang surface library in {Path.Combine( game, "core", "shaders" )}" );
			return Task.FromResult( OfflineResult.Failed );
		}

		return compiler.CompileOfflineAsync( source, null, log );
	}

	/// <summary>
	/// <see cref="CompileGameShaderAsync"/> for every <c>.slang</c> under a game project's folder <paramref name="folder"/>
	/// (its assets), a few at a time. Returns how many didn't fully compile.
	/// </summary>
	public static async Task<int> CompileGameShadersAsync( string game, string folder, Action<string> log, CancellationToken cancel = default )
	{
		if ( !Directory.Exists( folder ) ) return 0;
		var sources = SlangSources( folder ).Where( f => !IsHidden( folder, f ) ).ToList();
		var failed = 0;
		using var gate = new SemaphoreSlim( Math.Max( 1, Environment.ProcessorCount / 4 ) );
		await Task.WhenAll( sources.Select( async s =>
		{
			await gate.WaitAsync( cancel ).ConfigureAwait( false );
			try
			{
				var result = await CompileGameShaderAsync( game, s, log ).ConfigureAwait( false );
				if ( result == OfflineResult.Failed ) Interlocked.Increment( ref failed );
				if ( result is OfflineResult.Compiled or OfflineResult.Failed ) log?.Invoke( $"{Path.GetRelativePath( folder, s ).Replace( '\\', '/' )}: {result}" );
			}
			finally
			{
				gate.Release();
			}
		} ) ).ConfigureAwait( false );

		return failed;
	}

	static readonly Dictionary<string, (string Hash, string Description)> descriptions = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The surfaces and raw modules under a game project's folder <paramref name="folder"/> (its assets), each described for
	/// game code's typed class (<see cref="MaterialClassWriter.Describe"/>) by its path there (<c>shaders/rock.slang</c>):
	/// from its offline compile's layout when it's current, else one Slang reflection, kept while its text and the compiler
	/// are the same. One that doesn't compile is left out (its errors are the editor's to show). Pass modules and modules
	/// others import have none.
	/// </summary>
	public static List<(string Path, string Description)> DescribeGameSurfaces( string game, string folder )
	{
		var described = new List<(string, string)>();
		if ( !Directory.Exists( folder ) || GameCompiler( game ) is not { } compiler ) return described;
		foreach ( var source in SlangSources( folder ).Where( f => !IsHidden( folder, f ) ).Order( StringComparer.Ordinal ) )
		{
			var relative = Path.GetRelativePath( folder, source ).Replace( '\\', '/' );
			string text;
			try
			{
				text = File.ReadAllText( source );
			}
			catch ( IOException )
			{
				continue;
			}

			// Surfaces and raw modules only: a [Material] struct, no pass declarations
			if ( text.Contains( "sbox/pass.slangh", StringComparison.Ordinal ) || !System.Text.RegularExpressions.Regex.IsMatch( text, @"\[\s*Material\b" ) ) continue;

			var hash = $"{SlangCompiler.Version}|{Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( text ) ) )}";
			lock ( descriptions )
			{
				if ( descriptions.TryGetValue( source, out var known ) && known.Hash == hash )
				{
					if ( known.Description is not null ) described.Add( (relative, known.Description) );
					continue;
				}
			}

			string description = null;
			try
			{
				var shader = compiler.ReflectAsync( source ).GetAwaiter().GetResult();
				if ( shader.Material is not null ) description = MaterialClassWriter.Describe( shader, Path.ChangeExtension( relative, ".shader" ) );
			}
			catch ( ShaderCompileException )
			{
			}

			lock ( descriptions ) descriptions[source] = (hash, description);
			if ( description is not null ) described.Add( (relative, description) );
		}

		return described;
	}

	/// <summary>A file in a dot folder (<c>.source2</c>, <c>.vs</c>) or an <c>obj</c> folder under <paramref name="folder"/>: not the project's.</summary>
	static bool IsHidden( string folder, string file ) =>
		Path.GetRelativePath( folder, file ).Split( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar ).SkipLast( 1 ).Any( d => d.StartsWith( '.' ) || d.Equals( "obj", StringComparison.OrdinalIgnoreCase ) );

	/// <summary>The Slang sources under a folder: every <c>.slang</c>, and every <c>.shader</c> that's Slang by content (<see cref="ShaderFormat"/>).</summary>
	internal static IEnumerable<string> SlangSources( string folder ) => ShaderFormat.SlangSources( folder, SearchOption.AllDirectories );

	/// <summary>
	/// The Slang sources <see cref="CompileAll"/> compiles, each with where its offline compile goes (null: beside it): every
	/// <c>.slang</c> under the content roots' <c>shaders</c> folders but the surface library, every one in the addons' assets
	/// (<see cref="AddonSources"/>), and the surfaces converted from core <c>.shader</c> files at run time, compiled from the
	/// same text and kept in the core shaders.
	/// </summary>
	static List<(string Source, string Output)> Sources( string game, Action<string> log )
	{
		var shaders = Path.Combine( game, "core", "shaders" );
		var roots = ContentFiles.FindGame().Roots;
		var library = Path.Combine( shaders, "sbox" ) + Path.DirectorySeparatorChar;
		var sources = roots.Select( r => Path.Combine( r, "shaders" ) ).Prepend( shaders ).Distinct( StringComparer.OrdinalIgnoreCase ).Where( Directory.Exists )
			.SelectMany( SlangSources )
			.Where( f => !Path.GetFullPath( f ).StartsWith( library, StringComparison.OrdinalIgnoreCase ) )
			.Concat( AddonSources( game ) )
			.Select( Path.GetFullPath )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.Select( f => (Source: f, Output: (string)null) )
			.ToList();

		var converted = Path.Combine( game, ".source2", "borealisoffline", "converted" );
		foreach ( var file in SurfaceShaders.DefaultSurfaceFiles( shaders, roots, converted, log ) )
		{
			if ( Path.GetFullPath( file ).StartsWith( Path.GetFullPath( converted ), StringComparison.OrdinalIgnoreCase ) )
				sources.Add( (file, SlangOutput.PathOf( Path.Combine( shaders, Path.GetFileName( file ) ) )) );
		}

		return sources;
	}

	/// <summary>
	/// The Slang sources of the game folder's addons (<c>addons/*/Assets</c>, the sandbox game and the engine's own addons),
	/// anywhere under their assets as a game project's are (<see cref="CompileGameShadersAsync"/>), so the repo's games ship
	/// their shaders compiled as a published project does.
	/// </summary>
	static IEnumerable<string> AddonSources( string game )
	{
		var addons = Path.Combine( game, "addons" );
		if ( !Directory.Exists( addons ) ) return [];
		return Directory.EnumerateDirectories( addons ).Select( a => Path.Combine( a, "Assets" ) ).Where( Directory.Exists )
			.SelectMany( a => SlangSources( a ).Where( f => !IsHidden( a, f ) ) );
	}

	record struct DumpCombo( ulong Static, ulong Dynamic, string Name, string Spirv );
	record class DumpProgram( string Shader, string Stage, List<DumpCombo> Combos );

	/// <summary>
	/// <c>ShaderCompiler -slang --dump-spirv folder [source ...]</c>: the vertex and pixel stages of every surface
	/// <see cref="CompileAll"/> compiles (or of the sources in <paramref name="only"/>), compiled first where stale, as SPIR-V
	/// for <c>SboxBuild shader-stats</c>, in the shape it reads for a <c>.shader</c> (Sandbox.Engine's SpirvDump):
	/// <c>programs/{shader}.{stage}.json</c> per surface and stage with a combo per pass (Forward first), naming each module
	/// once in <c>spirv/{hash}.spv</c>. Specialization constants keep their defaults, as in a pipeline that isn't specialised
	/// for a material's features. A standalone pass module (<c>terrain.slang</c>) has a combo per combination of its combos,
	/// as its offline compile makes them. Returns how many sources failed.
	/// </summary>
	public static int DumpSurfaces( string game, string folder, IReadOnlyCollection<string> only, Action<string> log )
	{
		var failed = CompileAll( game, log );
		var shaders = Path.Combine( game, "core", "shaders" );
		var spirvFolder = Path.Combine( folder, "spirv" );
		var programFolder = Path.Combine( folder, "programs" );
		Directory.CreateDirectory( spirvFolder );
		Directory.CreateDirectory( programFolder );

		var wanted = only.Select( Path.GetFullPath ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		var compiler = new SlangCompiler( EngineFolders.NativeIn( game ), [shaders] ) { CacheFolder = Path.Combine( game, ".source2", "borealisoffline" ) };
		foreach ( var (source, output) in Sources( game, log ) )
		{
			// The converted surfaces are named as the core shader their offline compile sits beside
			var path = output is null ? Path.GetFullPath( source ) : Path.Combine( shaders, Path.GetFileName( source ) );
			if ( wanted.Count > 0 && !wanted.Contains( path ) ) continue;

			var text = File.ReadAllText( source );
			var name = Path.GetRelativePath( game, path ).Replace( '\\', '/' );
			if ( text.Contains( "sbox/pass.slangh", StringComparison.Ordinal ) )
			{
				failed += DumpPassModule( compiler, source, shaders, name, spirvFolder, programFolder, log );
				continue;
			}

			if ( SlangCompiler.IsRaw( text ) || !System.Text.RegularExpressions.Regex.IsMatch( text, @"\[\s*Material\b" ) ) continue;

			CompiledShader shader;
			try
			{
				shader = compiler.CompileSurface( source );
			}
			catch ( ShaderCompileException e )
			{
				failed++;
				log?.Invoke( $"{Path.GetFileName( source )}: {e.Errors}" );
				continue;
			}

			foreach ( var (stage, of) in new (string, Func<CompiledPass, CompiledStage>)[] { ("VS", p => p.Vertex), ("PS", p => p.Pixel) } )
			{
				var program = new DumpProgram( name, stage, [] );
				foreach ( var pass in shader.Passes.Values.OrderBy( p => p.Name != ShaderPasses.Forward ) )
				{
					if ( of( pass )?.Spirv is not { } spirv ) continue;
					program.Combos.Add( new DumpCombo( (ulong)program.Combos.Count, 0, pass.Name, WriteSpirv( spirvFolder, spirv ) ) );
				}

				WriteProgram( programFolder, program );
			}
		}

		return failed;
	}

	/// <summary>
	/// A standalone pass module's vertex and pixel stages, every combination its offline compile makes, each named by the
	/// combos that differ from their minimum. Returns how many failed.
	/// </summary>
	static int DumpPassModule( SlangCompiler compiler, string source, string shaders, string name, string spirvFolder, string programFolder, Action<string> log )
	{
		var failed = 0;
		var module = PassModule.Read( source, [shaders] );
		foreach ( var (stage, kind) in new[] { ("VS", StageKind.Vertex), ("PS", StageKind.Pixel) } )
		{
			var program = new DumpProgram( name, stage, [] );
			foreach ( var values in SlangCompiler.OfflineCombinations( module, kind ) )
			{
				CompiledPassStage compiled;
				try
				{
					compiled = compiler.CompilePass( module, kind, values );
				}
				catch ( ShaderCompileException e )
				{
					failed++;
					log?.Invoke( $"{Path.GetFileName( source )} {stage}: {e.Errors}" );
					continue;
				}

				if ( compiled.Stage?.Spirv is not { } spirv ) continue;
				var combo = string.Join( ' ', module.Combos( kind ).Where( c => values[c.Name] != c.Min ).Select( c => $"{c.Name}={values[c.Name]}" ) );
				program.Combos.Add( new DumpCombo( (ulong)program.Combos.Count, 0, combo.Length == 0 ? "default" : combo, WriteSpirv( spirvFolder, spirv ) ) );
			}

			WriteProgram( programFolder, program );
		}

		return failed;
	}

	static string WriteSpirv( string folder, byte[] spirv )
	{
		var hash = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( spirv ), 0, 16 ).ToLowerInvariant();
		var file = Path.Combine( folder, hash + ".spv" );
		if ( !File.Exists( file ) ) File.WriteAllBytes( file, spirv );
		return hash;
	}

	static void WriteProgram( string folder, DumpProgram program )
	{
		if ( program.Combos.Count == 0 ) return;
		File.WriteAllText( Path.Combine( folder, program.Shader.Replace( '/', '_' ).Replace( ':', '_' ) + "." + program.Stage + ".json" ), System.Text.Json.JsonSerializer.Serialize( program ) );
	}

	/// <summary>
	/// Make what the record definitions (<see cref="RecordDefinitions.File"/> in the core shaders) generate, where it isn't
	/// current: <see cref="RecordWriter.HlslFile"/> beside them and, in a source tree, <see cref="RecordWriter.CSharpFile"/>
	/// (whose change takes with the next managed build). Returns 0 on success.
	/// </summary>
	public static int UpdateRecords( string game, Action<string> log )
	{
		var shaders = Path.Combine( game, "core", "shaders" );
		try
		{
			var definitions = new SlangCompiler( EngineFolders.NativeIn( game ), [shaders] ).ReflectRecords();
			foreach ( var file in RecordWriter.Update( definitions, Path.Combine( game, "..", "engine" ), shaders ) )
				log?.Invoke( $"Records: wrote {Path.GetFullPath( file )}" );
			return 0;
		}
		catch ( ShaderCompileException e )
		{
			log?.Invoke( $"Records: {e.Message}" );
			return 1;
		}
	}
}
