using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Borealis.Source2;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;

namespace Borealis.Backend;

/// <summary>
/// The shader corpus gate (spec 2.3, docs/reference/shader-api.md): every shader authors' code depends on, compiled with
/// this engine's library and compiler and compared with the last accepted run. A shader that compiled then and doesn't
/// now is a break. The corpus is the core shaders (every Slang source under <c>core/shaders</c> but the library), the
/// BorealisLab surfaces, and what folders given by path hold: published Slang shaders (a <c>.slang</c> or a Slang
/// <c>.shader</c>, or its compile alone, from the source it embeds) and package <c>.shader_c</c> files (the pass module made from their embedded source,
/// as Borealis.Gpu draws them). Run by <c>ShaderCompiler -slang-corpus</c>, which <c>SboxBuild shader-corpus</c> runs in CI.
/// </summary>
public static class ShaderCorpus
{
	/// <summary>A corpus shader's outcome: <c>ok</c>, <c>fail</c> (with the first error), <c>refused</c> (a geometry stage), <c>skip</c> (not a shader), <c>error</c> (unreadable).</summary>
	public sealed record Result( string Id, string Status, string Detail );

	/// <summary>
	/// Compile the corpus of the game folder <paramref name="game"/> and the corpus folders <paramref name="folders"/>, write
	/// every result to <paramref name="results"/> (TSV), and compare with the baseline at <paramref name="baseline"/>; with
	/// <paramref name="update"/>, write the results as the new baseline (keeping its entries for folders not given here).
	/// Returns the number of breaks: shaders the baseline has as <c>ok</c> that don't compile now.
	/// </summary>
	public static int Run( string game, IReadOnlyList<string> folders, string results, string baseline, bool update, Action<string> log )
	{
		var clock = System.Diagnostics.Stopwatch.StartNew();
		game = Path.GetFullPath( game );
		var shaders = Path.Combine( game, "core", "shaders" );
		var work = Path.Combine( game, ".source2", "borealiscorpus" );
		var native = EngineFolders.NativeIn( game );
		SlangCompiler.CompileThreads = Environment.ProcessorCount;

		var (slang, packages) = Collect( game, folders, log );
		var extracted = Path.Combine( work, "extracted" );

		var found = new ConcurrentBag<Result>();
		var compiler = new SlangCompiler( native, [shaders] ) { CacheFolder = Path.Combine( work, "cache" ), UseOffline = false };
		var outputs = Path.Combine( work, "out" );
		Directory.CreateDirectory( outputs );
		Task.WhenAll( slang.Select( s => Task.Run( async () => found.Add( await Slang( compiler, s.Id, s.Path, outputs, extracted ).ConfigureAwait( false ) ) ) ) ).GetAwaiter().GetResult();

		var modules = Path.Combine( work, "modules" );
		Directory.CreateDirectory( modules );
		var compilers = new ConcurrentDictionary<string, SlangCompiler>( StringComparer.OrdinalIgnoreCase );
		Parallel.ForEach( packages, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, p =>
			found.Add( Package( compilers.GetOrAdd( p.Root, r => new SlangCompiler( native, [shaders, r] ) { CacheFolder = Path.Combine( work, "cache" ), UseOffline = false } ), p.Id, p.Path, shaders, p.Root, modules ) ) );

		var now = found.OrderBy( r => r.Id, StringComparer.Ordinal ).ToList();
		Directory.CreateDirectory( Path.GetDirectoryName( Path.GetFullPath( results ) ) );
		File.WriteAllLines( results, now.Select( r => $"{r.Id}\t{r.Status}\t{r.Detail}" ) );

		// The baseline: id and status a line, from the last accepted run
		var before = File.Exists( baseline )
			? File.ReadAllLines( baseline ).Where( l => l.Length > 0 && !l.StartsWith( '#' ) ).Select( l => l.Split( '\t' ) ).Where( p => p.Length >= 2 ).ToDictionary( p => p[0], p => p[1], StringComparer.Ordinal )
			: new Dictionary<string, string>( StringComparer.Ordinal );
		var current = now.ToDictionary( r => r.Id, StringComparer.Ordinal );
		var breaks = now.Where( r => r.Status != "ok" && before.GetValueOrDefault( r.Id ) == "ok" ).ToList();
		var fixedNow = now.Where( r => r.Status == "ok" && before.TryGetValue( r.Id, out var was ) && was != "ok" ).ToList();
		var added = now.Where( r => !before.ContainsKey( r.Id ) ).ToList();
		var gone = before.Keys.Where( id => !current.ContainsKey( id ) ).ToList();

		foreach ( var r in breaks ) log?.Invoke( $"BREAK {r.Id}: compiled before, now {r.Status}: {r.Detail}" );
		foreach ( var r in fixedNow ) log?.Invoke( $"now compiles: {r.Id} (was {before[r.Id]})" );
		foreach ( var r in added.Where( r => r.Status != "ok" && r.Status != "skip" ) ) log?.Invoke( $"new in the corpus, {r.Status}: {r.Id}: {r.Detail}" );
		var counts = string.Join( ", ", now.GroupBy( r => r.Status ).OrderBy( g => g.Key, StringComparer.Ordinal ).Select( g => $"{g.Count()} {g.Key}" ) );
		var kinds = string.Join( ", ", now.GroupBy( r => r.Id[..r.Id.IndexOf( '/' )] ).Select( g => $"{g.Key} {g.Count( r => r.Status == "ok" )}/{g.Count( r => r.Status != "skip" )}" ) );
		log?.Invoke( $"Shader corpus: {now.Count} shaders ({counts}; compiling by kind {kinds}); {breaks.Count} breaks, {fixedNow.Count} fixed, {added.Count} new, {gone.Count} not here, against {(before.Count == 0 ? "no baseline" : $"{before.Count} in the baseline")}, in {clock.Elapsed.TotalSeconds:F0} s" );

		if ( update )
		{
			// Entries of folders not given this run (the package sample on a machine without it) stay
			var lines = before.Where( b => !current.ContainsKey( b.Key ) && !b.Key.StartsWith( "core/", StringComparison.Ordinal ) && !b.Key.StartsWith( "lab/", StringComparison.Ordinal ) ).Select( b => (b.Key, b.Value) )
				.Concat( now.Select( r => (Key: r.Id, Value: r.Status) ) )
				.OrderBy( l => l.Key, StringComparer.Ordinal )
				.Select( l => $"{l.Key}\t{l.Value}" );
			File.WriteAllLines( baseline, ["# The shader corpus's last accepted results (SboxBuild shader-corpus --update): id, then ok, fail, refused, skip or error", .. lines] );
			log?.Invoke( $"Shader corpus: baseline written to {baseline}" );
			return 0;
		}

		return breaks.Count;
	}

	/// <summary>
	/// What the corpus of <paramref name="game"/> and <paramref name="folders"/> holds, by id: the Slang shaders (sources, a
	/// <c>.slang</c> or a <c>.shader</c> that's Slang by content, and compiles shipped alone, compiled from the source they
	/// embed) and the package <c>.shader_c</c> files (vfx compiles, not <c>SLGC</c>). Told by content, never by extension.
	/// </summary>
	internal static (List<(string Id, string Path)> Slang, List<(string Id, string Path, string Root)> Packages) Collect( string game, IReadOnlyList<string> folders, Action<string> log )
	{
		game = Path.GetFullPath( game );
		var shaders = Path.Combine( game, "core", "shaders" );
		var slang = new List<(string Id, string Path)>();
		var packages = new List<(string Id, string Path, string Root)>();
		var library = Path.Combine( shaders, "sbox" ) + Path.DirectorySeparatorChar;
		foreach ( var file in ShaderFormat.SlangSources( shaders, SearchOption.AllDirectories ).Where( f => !f.StartsWith( library, StringComparison.OrdinalIgnoreCase ) ).Order( StringComparer.Ordinal ) )
			slang.Add( ($"core/{Relative( shaders, file )}", file) );

		var lab = Path.Combine( Path.GetDirectoryName( game ), "engine", "Launcher", "BorealisLab", "Surfaces" );
		foreach ( var file in ShaderFormat.SlangSources( lab ).Order( StringComparer.Ordinal ) ) slang.Add( ($"lab/{Path.GetFileName( file )}", file) );

		foreach ( var folder in folders.Select( Path.GetFullPath ) )
		{
			if ( !Directory.Exists( folder ) )
			{
				log?.Invoke( $"Shader corpus: no folder {folder}" );
				continue;
			}

			var name = Path.GetFileName( Path.TrimEndingDirectorySeparator( folder ) );
			foreach ( var file in Directory.EnumerateFiles( folder, "*", SearchOption.AllDirectories ).Order( StringComparer.Ordinal ) )
			{
				var id = $"{name}/{Relative( folder, file )}";
				var compiled = file.EndsWith( SlangOutput.Suffix, StringComparison.OrdinalIgnoreCase );
				if ( file.EndsWith( ".slang", StringComparison.OrdinalIgnoreCase ) || (file.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase ) && ShaderFormat.IsSlangSourceFile( file )) ) slang.Add( ($"slang/{id}", file) );
				else if ( compiled && ShaderFormat.IsSlangCompiledPath( file ) )
				{
					// A Slang compile: its source beside it is in the corpus already; alone, it compiles from the source it embeds
					if ( !File.Exists( file[..^SlangOutput.Suffix.Length] ) ) slang.Add( ($"slang/{id[..^SlangOutput.Suffix.Length]}", file) );
				}
				else if ( file.EndsWith( ".shader_c", StringComparison.OrdinalIgnoreCase ) ) packages.Add( ($"package/{id}", file, folder) );
			}
		}

		return (slang, packages);
	}

	/// <summary>A Slang source (or a compiled one shipped alone) compiled offline, every pass or combo, into a scratch output.</summary>
	static async Task<Result> Slang( SlangCompiler compiler, string id, string path, string outputs, string extracted )
	{
		var source = path;
		if ( path.EndsWith( SlangOutput.Suffix, StringComparison.OrdinalIgnoreCase ) )
		{
			source = SlangOutput.SourceOf( path, extracted, out var problem );
			if ( source is null ) return new Result( id, "error", problem );
		}

		var errors = new ConcurrentQueue<string>();
		try
		{
			var output = Path.Combine( outputs, $"{Path.GetFileNameWithoutExtension( source )}.{Hash( id )}.slang_c" );
			return await compiler.CompileOfflineAsync( source, output, errors.Enqueue ).ConfigureAwait( false ) switch
			{
				OfflineResult.Compiled or OfflineResult.UpToDate => new Result( id, "ok", "" ),
				OfflineResult.Skipped => new Result( id, "skip", "not a shader (a library module)" ),
				_ => new Result( id, "fail", First( errors.FirstOrDefault() ?? "nothing compiled" ) ),
			};
		}
		catch ( Exception e ) when ( e is ShaderCompileException or IOException or InvalidOperationException )
		{
			return new Result( id, "fail", First( e.Message ) );
		}
	}

	/// <summary>A package <c>.shader_c</c>: the pass module made from its embedded source, each stage at its combos' first values and each combo at its last.</summary>
	static Result Package( SlangCompiler compiler, string id, string path, string shaders, string root, string modules )
	{
		ShaderFile shader;
		try
		{
			shader = ShaderFile.LoadOutdated( path );
		}
		catch ( Exception e )
		{
			return new Result( id, "error", First( $"unreadable: {e.Message}" ) );
		}

		if ( shader?.EmbeddedSource is not { } source ) return new Result( id, "skip", "no embedded source" );
		if ( PassConverter.RefuseEmbedded( source ) is { } refused ) return new Result( id, "refused", First( refused ) );

		try
		{
			var stem = Regex.Replace( Path.GetFileName( path ).Split( '.' )[0], "[^a-zA-Z0-9_]", "_" );
			var text = PassConverter.FromEmbedded( source, (shader.Features?.StaticCombos ?? []).Select( c => (c.Name, c.Min, c.Max) ), stem );
			var module = Path.Combine( modules, $"{stem}.{Hash( id )}.slang" );
			File.WriteAllText( module, text );
			var pass = PassModule.FromText( module, text, [shaders, root] );
			var stages = 0;
			foreach ( var stage in new[] { StageKind.Vertex, StageKind.Pixel, StageKind.Compute } )
			{
				if ( !pass.Has( stage ) ) continue;
				var combos = pass.Combos( stage );
				var sets = new List<Dictionary<string, int>> { combos.ToDictionary( c => c.Name, c => c.Min ) };
				foreach ( var combo in combos.Where( c => c.Max > c.Min ) )
				{
					var set = combos.ToDictionary( c => c.Name, c => c.Min );
					set[combo.Name] = combo.Max;
					sets.Add( set );
				}

				foreach ( var values in sets.Where( v => pass.Has( stage, v ) ) )
				{
					compiler.CompilePass( pass, stage, values );
					stages++;
				}
			}

			return stages == 0 ? new Result( id, "skip", "no stage to compile" ) : new Result( id, "ok", "" );
		}
		catch ( ShaderCompileException e )
		{
			return new Result( id, "fail", First( e.Errors ) );
		}
		catch ( Exception e ) when ( e is IOException or InvalidOperationException or ArgumentException or FormatException )
		{
			return new Result( id, "fail", First( e.Message ) );
		}
	}

	static string Relative( string folder, string file ) => Path.GetRelativePath( folder, file ).Replace( '\\', '/' );

	static string Hash( string text ) => Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( text ) ), 0, 4 ).ToLowerInvariant();

	/// <summary>The first error line of a compiler's messages, on one line.</summary>
	static string First( string errors )
	{
		var lines = errors.Replace( "\r", "" ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		var at = Array.FindIndex( lines, l => l.Contains( "error", StringComparison.OrdinalIgnoreCase ) );
		var line = at >= 0 ? lines[at] : lines.FirstOrDefault() ?? "";

		// Slang's multi-line form: the message under the caret says what (the member, the type)
		var caret = at < 0 ? null : lines.Skip( at + 1 ).TakeWhile( l => !l.Contains( "error", StringComparison.OrdinalIgnoreCase ) || l.StartsWith( '|' ) ).FirstOrDefault( l => l.StartsWith( '|' ) && l.TrimStart( '|', ' ' ).StartsWith( '^' ) );
		if ( caret is not null ) line += ": " + caret.TrimStart( '|', ' ', '^' ).Trim();
		return line.Replace( '\t', ' ' ).Length > 300 ? line.Replace( '\t', ' ' )[..300] : line.Replace( '\t', ' ' );
	}
}
