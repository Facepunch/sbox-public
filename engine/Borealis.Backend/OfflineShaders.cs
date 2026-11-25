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
		if ( UpdateRecords( game, log ) != 0 ) return 1;
		var sources = Sources( game, log );
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

	/// <summary>
	/// The Slang sources <see cref="CompileAll"/> compiles, each with where its offline compile goes (null: beside it): every
	/// <c>.slang</c> under the content roots' <c>shaders</c> folders but the surface library, and the surfaces converted from
	/// core <c>.shader</c> files at run time, compiled from the same text and kept in the core shaders.
	/// </summary>
	static List<(string Source, string Output)> Sources( string game, Action<string> log )
	{
		var shaders = Path.Combine( game, "core", "shaders" );
		var roots = ContentFiles.FindGame().Roots;
		var library = Path.Combine( shaders, "sbox" ) + Path.DirectorySeparatorChar;
		var sources = roots.Select( r => Path.Combine( r, "shaders" ) ).Prepend( shaders ).Distinct( StringComparer.OrdinalIgnoreCase ).Where( Directory.Exists )
			.SelectMany( f => Directory.GetFiles( f, "*.slang", SearchOption.AllDirectories ) )
			.Where( f => !Path.GetFullPath( f ).StartsWith( library, StringComparison.OrdinalIgnoreCase ) )
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
