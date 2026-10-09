using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Register pressure for every shader: dumps each combo's SPIR-V with <c>ShaderCompiler --dump-spirv</c>, compiles
/// it for AMD GPUs with the pipeline compiler from the Radeon GPU Analyzer and writes the worst combo per shader and
/// stage to <c>docs/shaders/shader-stats.md</c>, so a change in VGPRs or occupancy shows up in the diff.
/// </summary>
internal class ShaderStats( string[] asics, string[] shaders )
{
	const string RgaVersion = "2.14.2";
	const string RgaUrl = $"https://github.com/GPUOpen-Tools/radeon_gpu_analyzer/releases/download/{RgaVersion}/rga-windows-x64-{RgaVersion}.zip";
	const string RgaDir = $"src/thirdparty/rga/{RgaVersion}";
	const string Output = "docs/shaders/shader-stats.md";

	public static readonly string[] DefaultAsics = ["gfx1100", "gfx1150", "gfx1200"];

	/// <summary>
	/// Occupancy inputs per target, from LLVM's AMDGPU backend (Feature1536VGPRs in AMDGPU.td; the granule and
	/// totals in AMDGPUBaseInfo getVGPRAllocGranule and AMDGPUTargetParser getTotalNumVGPRs). GFX11 and GFX12
	/// both run at most 16 waves per SIMD.
	/// </summary>
	static readonly Dictionary<string, (string Name, bool LargeVgprFile)> Targets = new()
	{
		["gfx1100"] = ("RDNA 3 (RX 7900)", true),
		["gfx1101"] = ("RDNA 3 (RX 7800/7700)", true),
		["gfx1102"] = ("RDNA 3 (RX 7600)", false),
		["gfx1103"] = ("RDNA 3 (Radeon 780M)", false),
		["gfx1150"] = ("RDNA 3.5 (Radeon 890M)", false),
		["gfx1151"] = ("RDNA 3.5 (Radeon 8060S)", true),
		["gfx1152"] = ("RDNA 3.5 (Radeon 860M)", false),
		["gfx1200"] = ("RDNA 4 (RX 9060)", true),
		["gfx1201"] = ("RDNA 4 (RX 9070)", true),
	};

	const int MaxWaves = 16;

	static readonly (string Stage, string Title)[] Stages =
	[
		("PS", "Pixel shaders"),
		("VS", "Vertex shaders"),
		("GS", "Geometry shaders"),
		("CS", "Compute shaders"),
	];

	record class Program( string Shader, string Stage, List<Combo> Combos );
	record struct Combo( ulong Static, ulong Dynamic, string Name, string Spirv );

	record class Stats( int Vgpr, int Sgpr, int Lds, int Scratch, int Wave, string Error = null );

	internal ExitCode Run() => BuildDisplay.Run( "Shader stats", () =>
	{
		if ( !OperatingSystem.IsWindows() )
		{
			Log.Error( "shader-stats needs Windows: it drives ShaderCompiler.exe and the Windows build of RGA." );
			return ExitCode.Failure;
		}

		var unknown = asics.Where( x => !Targets.ContainsKey( x ) ).ToArray();
		if ( unknown.Length > 0 )
		{
			Log.Error( $"Unsupported --asic {string.Join( ", ", unknown )}. Known: {string.Join( ", ", Targets.Keys )}" );
			return ExitCode.Failure;
		}

		var root = Directory.GetCurrentDirectory();
		var game = Path.Combine( root, "game" );
		var cache = Path.Combine( game, ".source2", "shaderstats" );

		var rga = FindRga( root );
		if ( rga is null ) return ExitCode.Failure;

		// RGA's offline Vulkan mode runs this with verbose LLVM IR logging that it then parses, five times slower than
		// compiling directly; everything we report is in the pipeline ELF's metadata anyway
		var llpc = Path.Combine( Path.GetDirectoryName( rga ), "utils", "amdllpc.exe" );
		if ( !File.Exists( llpc ) )
		{
			Log.Error( $"{llpc} is missing - RGA {RgaVersion} ships it." );
			return ExitCode.Failure;
		}

		if ( !DumpSpirv( game, cache ) ) return ExitCode.Failure;

		var programs = LoadPrograms( cache );
		if ( programs.Count == 0 )
		{
			Log.Error( "ShaderCompiler dumped no SPIR-V." );
			return ExitCode.Failure;
		}

		var stats = Analyze( llpc, cache, programs );

		var markdown = WriteMarkdown( programs, stats );
		var output = Path.Combine( root, Output );
		Directory.CreateDirectory( Path.GetDirectoryName( output ) );
		File.WriteAllText( output, markdown );

		Log.Summary( $"Wrote {Output}: {programs.Count} programs, {programs.Sum( x => x.Combos.Count ):n0} combos." );
		return ExitCode.Success;
	} );

	/// <summary>
	/// RGA_PATH, else the pinned release under src/thirdparty, downloaded the first time.
	/// </summary>
	static string FindRga( string root )
	{
		var env = Environment.GetEnvironmentVariable( "RGA_PATH" );
		if ( !string.IsNullOrEmpty( env ) )
		{
			if ( File.Exists( env ) ) return env;
			Log.Error( $"RGA_PATH is set but {env} doesn't exist." );
			return null;
		}

		var dir = Path.Combine( root, RgaDir );
		var exe = Path.Combine( dir, "rga.exe" );
		if ( File.Exists( exe ) ) return exe;

		BuildDisplay.Status( $"Download Radeon GPU Analyzer {RgaVersion}" );
		var zip = Path.Combine( Path.GetTempPath(), $"rga-windows-x64-{RgaVersion}.zip" );
		try
		{
			using ( var http = new HttpClient { Timeout = TimeSpan.FromMinutes( 30 ) } )
			using ( var response = http.GetAsync( RgaUrl, HttpCompletionOption.ResponseHeadersRead ).GetAwaiter().GetResult() )
			{
				response.EnsureSuccessStatusCode();
				var total = response.Content.Headers.ContentLength ?? 0;
				using var source = response.Content.ReadAsStream();
				using var target = File.Create( zip );
				var buffer = new byte[1 << 20];
				long read = 0;
				for ( int n; (n = source.Read( buffer )) > 0; )
				{
					target.Write( buffer, 0, n );
					read += n;
					BuildDisplay.Progress( read, total, $"{Utility.FormatSize( read )} / {Utility.FormatSize( total )}" );
				}
			}

			BuildDisplay.Status( "Extract Radeon GPU Analyzer" );
			ZipFile.ExtractToDirectory( zip, dir, true );
		}
		catch ( Exception e )
		{
			Log.Error( $"Couldn't fetch RGA from {RgaUrl}: {e.Message}. Install it and set RGA_PATH to rga.exe instead." );
			return null;
		}
		finally
		{
			File.Delete( zip );
		}

		if ( File.Exists( exe ) ) return exe;
		Log.Error( $"{RgaUrl} didn't contain rga.exe" );
		return null;
	}

	/// <summary>
	/// Compile through the native shader cache, so only changed combos cost anything, writing SPIR-V instead of .shader_c.
	/// </summary>
	bool DumpSpirv( string game, string cache )
	{
		var compiler = Path.Combine( game, "bin", "managed", "ShaderCompiler.exe" );
		if ( !File.Exists( compiler ) )
		{
			Log.Error( $"Shader compiler not found at {compiler} - build the engine first." );
			return false;
		}

		// A full run replaces every manifest so deleted shaders drop out; a partial one only its own
		var programDir = Path.Combine( cache, "programs" );
		var targets = shaders.Select( x => Path.GetFullPath( x ) ).ToArray();
		if ( targets.Length == 0 )
		{
			if ( Directory.Exists( programDir ) ) Directory.Delete( programDir, true );
		}
		else if ( Directory.Exists( programDir ) )
		{
			foreach ( var target in targets )
			{
				var prefix = Path.GetRelativePath( game, target ).Replace( '\\', '/' ).Replace( '/', '_' ) + ".";
				foreach ( var file in Directory.GetFiles( programDir, prefix + "*.json" ) ) File.Delete( file );
			}
		}

		var files = targets.Length == 0 ? "*" : string.Join( ' ', targets.Select( x => $"\"{x}\"" ) );
		var failed = new List<string>();
		string current = null;
		BuildDisplay.Status( "Compile shaders and dump SPIR-V" );

		var success = Utility.RunProcess( compiler, $"{files} --dump-spirv \"{cache}\"", game, timeoutMs: 7_200_000,
			onDataReceived: ( _, e ) =>
			{
				if ( e.Data is null ) return;
				var match = Regex.Match( e.Data, @"^\((\d+)/(\d+)\)\s*(.*)$" );
				if ( match.Success )
				{
					current = match.Groups[3].Value;
					BuildDisplay.Progress( int.Parse( match.Groups[1].Value ) - 1, int.Parse( match.Groups[2].Value ), current );
				}
				else if ( e.Data.Contains( "Compile failed." ) && current is not null )
				{
					lock ( failed ) failed.Add( current );
				}
			} );

		// A broken shader shouldn't hide the stats for the rest; build-shaders is what fails on it
		foreach ( var shader in failed ) Log.Warning( $"{shader} failed to compile and is missing from the stats." );
		if ( !success && failed.Count == 0 )
		{
			Log.Error( "ShaderCompiler failed." );
			return false;
		}

		return true;
	}

	static List<Program> LoadPrograms( string cache )
	{
		var dir = Path.Combine( cache, "programs" );
		if ( !Directory.Exists( dir ) ) return [];

		return Directory.GetFiles( dir, "*.json" )
			.Select( x => JsonSerializer.Deserialize<Program>( File.ReadAllText( x ) ) )
			.Where( x => Stages.Any( s => s.Stage == x.Stage ) )
			.OrderBy( x => x.Shader, StringComparer.OrdinalIgnoreCase ).ThenBy( x => x.Stage )
			.ToList();
	}

	/// <summary>
	/// Every module on the first target, so its worst combo is exact. The other targets only get each stage's default
	/// and that worst combo: complex.shader alone is ~1900 big modules, too slow to repeat per target.
	/// </summary>
	Dictionary<(string Asic, string Spirv), Stats> Analyze( string llpc, string cache, List<Program> programs )
	{
		var results = new Dictionary<(string, string), Stats>();
		var primary = asics[0];

		AnalyzeModules( llpc, cache, primary, programs.SelectMany( p => p.Combos.Select( c => c.Spirv ) ), results );

		var picked = programs.SelectMany( p =>
		{
			var row = Summarize( p, primary, results );
			return new[] { row.Default, row.Worst }.Where( c => c.Spirv is not null ).Select( c => c.Spirv );
		} ).ToArray();

		foreach ( var asic in asics.Skip( 1 ) )
			AnalyzeModules( llpc, cache, asic, picked, results );

		var errors = results.Values.Count( x => x.Error is not null );
		if ( errors > 0 ) Log.Warning( $"amdllpc failed on {errors} modules; they're marked in the report." );

		return results;
	}

	/// <summary>
	/// Cached by content under the RGA version, so a rerun only compiles modules that changed.
	/// </summary>
	static void AnalyzeModules( string llpc, string cache, string asic, IEnumerable<string> modules, Dictionary<(string, string), Stats> results )
	{
		var dir = Path.Combine( cache, "llpc", RgaVersion, asic );
		Directory.CreateDirectory( dir );

		var jobs = new List<(string Spirv, string File)>();
		var unique = modules.Distinct().ToArray();
		foreach ( var spirv in unique )
		{
			var file = Path.Combine( dir, spirv + ".json" );
			if ( File.Exists( file ) ) results[(asic, spirv)] = JsonSerializer.Deserialize<Stats>( File.ReadAllText( file ) );
			else jobs.Add( (spirv, file) );
		}

		BuildDisplay.Status( $"Analyze {jobs.Count:n0} modules for {asic} ({unique.Length - jobs.Count:n0} cached)" );

		// Biggest first, so the long complex.shader modules don't end up as a tail on a few cores
		jobs = jobs.OrderByDescending( x => new FileInfo( Path.Combine( cache, "spirv", x.Spirv + ".spv" ) ).Length ).ToList();

		int done = 0;
		var finished = new ConcurrentBag<(string Spirv, Stats Stats)>();
		Parallel.ForEach( Partitioner.Create( jobs, EnumerablePartitionerOptions.NoBuffering ), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, job =>
		{
			var stats = Compile( llpc, Path.Combine( cache, "spirv", job.Spirv + ".spv" ), asic, Path.Combine( cache, "tmp", $"{asic}_{job.Spirv}" ) );
			File.WriteAllText( job.File, JsonSerializer.Serialize( stats ) );
			finished.Add( (job.Spirv, stats) );

			var n = Interlocked.Increment( ref done );
			BuildDisplay.Progress( n, jobs.Count, $"{n:n0} / {jobs.Count:n0}" );
		} );

		foreach ( var (spirv, stats) in finished ) results[(asic, spirv)] = stats;
	}

	static Stats Compile( string llpc, string spirv, string asic, string temp )
	{
		Directory.CreateDirectory( temp );
		try
		{
			// gfx1151 -> 11.5.1
			var digits = asic["gfx".Length..];
			var gfxip = $"{digits[..^2]}.{digits[^2]}.{digits[^1]}";
			var elf = Path.Combine( temp, "pipeline.elf" );

			var info = new ProcessStartInfo( llpc, $"--auto-layout-desc --gfxip={gfxip} -o=\"{elf}\" \"{spirv}\"" )
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			info.Environment["TEMP"] = temp;
			info.Environment["TMP"] = temp;

			using var process = Process.Start( info );
			var stdout = process.StandardOutput.ReadToEndAsync();
			var stderr = process.StandardError.ReadToEndAsync();
			if ( !process.WaitForExit( 600_000 ) )
			{
				process.Kill( true );
				return new Stats( 0, 0, 0, 0, 0, "timed out" );
			}

			if ( process.ExitCode != 0 || !File.Exists( elf ) )
			{
				var log = (stdout.Result + stderr.Result).Trim().Split( '\n' ).LastOrDefault( x => x.Contains( "rror" ) ) ?? $"exit code {process.ExitCode}";
				return new Stats( 0, 0, 0, 0, 0, log.Trim() );
			}

			var bytes = File.ReadAllBytes( elf );
			int Read( string key ) => ReadMetadata( bytes, key ) ?? throw new InvalidDataException( $"{key} missing from the pipeline metadata of {spirv}" );
			return new Stats( Read( ".vgpr_count" ), Read( ".sgpr_count" ), Read( ".lds_size" ), Read( ".scratch_memory_size" ), Read( ".wavefront_size" ) );
		}
		finally
		{
			try { Directory.Delete( temp, true ); } catch ( IOException ) { }
		}
	}

	/// <summary>
	/// An unsigned value from the ELF's PAL metadata note, which is msgpack: the key as a fixstr, then a positive fixint
	/// or uint8/16/32. Each stage is compiled as its own pipeline, so the first hardware stage is the only one.
	/// </summary>
	static int? ReadMetadata( byte[] elf, string name )
	{
		var key = Encoding.ASCII.GetBytes( name );
		var at = elf.AsSpan().IndexOf( key );
		if ( at < 1 || elf[at - 1] != 0xa0 + key.Length ) return null;

		var value = elf.AsSpan( at + key.Length );
		if ( value.Length < 5 ) return null;

		return value[0] switch
		{
			< 0x80 => value[0],
			0xcc => value[1],
			0xcd => BinaryPrimitives.ReadUInt16BigEndian( value[1..] ),
			0xce => (int)BinaryPrimitives.ReadUInt32BigEndian( value[1..] ),
			_ => null,
		};
	}

	/// <summary>
	/// VGPR-limited waves per SIMD, as LLVM's getNumWavesPerEUWithNumVGPRs works it out.
	/// </summary>
	static int Waves( string asic, int vgprs, int wave )
	{
		var large = Targets[asic].LargeVgprFile;
		var wave32 = wave == 32;
		var total = large ? (wave32 ? 1536 : 768) : (wave32 ? 1024 : 512);
		var granule = large ? (wave32 ? 24 : 12) : (wave32 ? 16 : 8);
		if ( vgprs < granule ) return MaxWaves;

		var allocated = (vgprs + granule - 1) / granule * granule;
		return Math.Clamp( total / allocated, 1, MaxWaves );
	}

	record struct Row( Program Program, Combo Default, Stats DefaultStats, Combo Worst, Stats WorstStats, int Modules, int Errors );

	/// <summary>
	/// Default is the first enumerated combo; worst is the one with the most VGPRs, then scratch, then SGPRs, then the lowest index.
	/// </summary>
	static Row Summarize( Program program, string asic, Dictionary<(string, string), Stats> stats )
	{
		Stats Of( Combo combo ) => stats.GetValueOrDefault( (asic, combo.Spirv) );

		var analyzed = program.Combos.Where( x => Of( x ) is { Error: null } ).ToList();
		var errors = program.Combos.Select( x => x.Spirv ).Distinct().Count( x => stats.GetValueOrDefault( (asic, x) )?.Error is not null );
		var modules = program.Combos.Select( x => x.Spirv ).Distinct().Count();

		var first = program.Combos[0];
		var worst = analyzed
			.OrderByDescending( x => Of( x ).Vgpr )
			.ThenByDescending( x => Of( x ).Scratch )
			.ThenByDescending( x => Of( x ).Sgpr )
			.ThenBy( x => x.Static ).ThenBy( x => x.Dynamic )
			.FirstOrDefault();

		return new Row( program, first, Of( first ), worst, analyzed.Count > 0 ? Of( worst ) : null, modules, errors );
	}

	string WriteMarkdown( List<Program> programs, Dictionary<(string, string), Stats> stats )
	{
		var primary = asics[0];
		var rows = programs.Select( p => Summarize( p, primary, stats ) ).ToList();

		var md = new StringBuilder();
		md.AppendLine( "# Shader stats" );
		md.AppendLine();
		md.AppendLine( "> Generated by `tools/ShaderStats.bat` (`SboxBuild shader-stats`); don't edit it by hand. Rerun it after a shader" );
		md.AppendLine( "> change and commit the result with the `.shader_c`, so register pressure changes show up in review." );
		md.AppendLine();
		md.AppendLine( $"Every combo of every shader under `game/` is compiled to the SPIR-V that ships in its `.shader_c` (specialization" );
		md.AppendLine( "constants baked in), then to AMD ISA by `amdllpc`, the AMDVLK pipeline compiler behind the offline Vulkan mode of the" );
		md.AppendLine( $"[Radeon GPU Analyzer](https://gpuopen.com/rga/) {RgaVersion}. Each stage is compiled on its own without the real" );
		md.AppendLine( "pipeline state, so treat the numbers as close, not exact. Columns:" );
		md.AppendLine();
		md.AppendLine( "- **Worst** is the combo with the most VGPRs; the combo is named by the values that differ from the default." );
		md.AppendLine( "  **Default** is the first combo, every combo at its minimum." );
		md.AppendLine( $"- **Waves** is VGPR-limited occupancy in waves per SIMD, out of {MaxWaves}, using LLVM's allocation rules for the" );
		md.AppendLine( "  target and the wave size the compiler picked (**Wave**). Fewer waves means less latency hiding." );
		md.AppendLine( "- **Scratch** is per-thread scratch memory: register spills, or arrays the compiler couldn't keep in registers." );
		md.AppendLine( "  It goes through memory, so anything but 0 is worth fixing." );
		md.AppendLine( "- **Combos** is how many combos the stage compiles, **Modules** how many distinct SPIR-V modules they produce." );
		md.AppendLine();
		md.AppendLine( $"Targets: {string.Join( ", ", asics.Select( x => $"`{x}` {Targets[x].Name}" ) )}." );
		md.AppendLine();

		md.AppendLine( $"## Lowest occupancy ({primary})" );
		md.AppendLine();
		md.AppendLine( "| Shader | Stage | VGPR | Waves | Scratch | Worst combo |" );
		md.AppendLine( "|---|---|--:|--:|--:|---|" );
		foreach ( var row in rows.Where( x => x.WorstStats is not null )
			.OrderBy( x => Waves( primary, x.WorstStats.Vgpr, x.WorstStats.Wave ) )
			.ThenByDescending( x => x.WorstStats.Vgpr )
			.ThenBy( x => x.Program.Shader ).Take( 20 ) )
		{
			var s = row.WorstStats;
			md.AppendLine( $"| {Name( row.Program )} | {row.Program.Stage} | {s.Vgpr} | {Waves( primary, s.Vgpr, s.Wave )} | {Scratch( s.Scratch )} | {ComboName( row.Worst )} |" );
		}
		md.AppendLine();

		md.AppendLine( $"## {Targets[primary].Name} - {primary}" );
		md.AppendLine();
		foreach ( var (stage, title) in Stages )
		{
			var stageRows = rows.Where( x => x.Program.Stage == stage ).ToList();
			if ( stageRows.Count == 0 ) continue;

			var compute = stage == "CS";
			md.AppendLine( $"### {title}" );
			md.AppendLine();
			md.AppendLine( compute
				? "| Shader | Combos | Modules | Default VGPR | Worst VGPR | SGPR | Wave | Waves | Scratch | LDS | Worst combo |"
				: "| Shader | Combos | Modules | Default VGPR | Worst VGPR | SGPR | Wave | Waves | Scratch | Worst combo |" );
			md.AppendLine( compute
				? "|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|---|"
				: "|---|--:|--:|--:|--:|--:|--:|--:|--:|---|" );

			foreach ( var row in stageRows )
			{
				var s = row.WorstStats;
				var name = Name( row.Program ) + (row.Errors > 0 ? $" ({row.Errors} failed to compile)" : "");
				if ( s is null )
				{
					md.AppendLine( $"| {name} | {row.Program.Combos.Count:n0} | {row.Modules:n0} | | | | | | | {(compute ? "| " : "")}|" );
					continue;
				}

				var line = $"| {name} | {row.Program.Combos.Count:n0} | {row.Modules:n0} | {row.DefaultStats?.Vgpr.ToString() ?? "?"} | {s.Vgpr} | {s.Sgpr} | {s.Wave} | {Waves( primary, s.Vgpr, s.Wave )} | {Scratch( s.Scratch )} |";
				if ( compute ) line += $" {Kb( s.Lds )} |";
				md.AppendLine( $"{line} {ComboName( row.Worst )} |" );
			}
			md.AppendLine();
		}

		if ( asics.Length > 1 )
		{
			md.AppendLine( "## All targets" );
			md.AppendLine();
			md.AppendLine( $"VGPRs (waves per SIMD) of each stage's {primary} worst combo, compiled for every target. Only {primary} is searched" );
			md.AppendLine( "for the worst combo, so on another target a different combo can be slightly worse." );
			md.AppendLine();
			md.AppendLine( $"| Shader | Stage | {string.Join( " | ", asics )} |" );
			md.AppendLine( $"|---|---|{string.Concat( asics.Select( _ => "--:|" ) )}" );
			foreach ( var row in rows.Where( x => x.WorstStats is not null ) )
			{
				var cells = asics.Select( asic => stats.GetValueOrDefault( (asic, row.Worst.Spirv) ) is { Error: null } s
					? $"{s.Vgpr} ({Waves( asic, s.Vgpr, s.Wave )})"
					: "" );
				md.AppendLine( $"| {Name( row.Program )} | {row.Program.Stage} | {string.Join( " | ", cells )} |" );
			}
			md.AppendLine();
		}

		return md.ToString().ReplaceLineEndings( "\n" );
	}

	static string Name( Program program )
	{
		var name = program.Shader.EndsWith( ".shader" ) ? program.Shader[..^".shader".Length] : program.Shader;
		return name.StartsWith( "core/shaders/" ) ? name["core/shaders/".Length..] : name;
	}

	static string ComboName( Combo combo ) => string.IsNullOrEmpty( combo.Name ) ? "default" : $"`{combo.Name}`";
	static string Scratch( int bytes ) => bytes == 0 ? "0" : $"**{bytes} B**";
	static string Kb( int bytes ) => bytes == 0 ? "0" : $"{bytes / 1024.0:0.#} KB";
}
