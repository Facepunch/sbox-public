using Microsoft.CodeAnalysis;
using Sandbox;
using System.Text.Json;

namespace Facepunch.PackageBuild;

/// <summary>
/// Headless, managed-only package compiler shipped inside the engine build (game/bin/managed). Takes a package
/// revision's <c>.cll</c> code archives, compiles them with <em>this build's own</em> Sandbox.Compiling +
/// Sandbox.Generator against this build's managed assemblies, and writes the resulting <c>.dll</c>(s) plus a
/// JSON result.
///
/// The sbox.web CLL compile worker downloads the engine build for a given commit and invokes this tool, so each
/// package is compiled with the exact engine version it targets — the RPC/serialization hashes always match that
/// engine's clients. See sbox.web <c>Docs/CompileToolchainVersioning.md</c>.
///
/// It only drives the compiler (no engine/native boot), so the worker needs just the managed assemblies to run it.
///
/// <code>
/// PackageBuild --archive &lt;file.cll&gt; [--archive ...] --out &lt;dir&gt; [--refs &lt;dir&gt; ...]
///   --archive, -a   (>=1) A .cll code archive to compile — the package's own code plus any bundled deps.
///   --out, -o       (required) Directory to write compiled .dll files into.
///   --refs, -r      A directory of managed reference DLLs to compile against. Repeatable.
///                   Defaults to this tool's own directory (the build's bin/managed).
/// </code>
///
/// stdout: one JSON object — <c>{ success, dlls: [{ assemblyName, file }], errors: [] }</c>.
/// stderr: human-readable progress/diagnostics. Exit code 0 = success, 1 = failure or bad args.
/// </summary>
static class Program
{
	public static async Task<int> Main( string[] args )
	{
		try
		{
			return await RunAsync( args );
		}
		catch ( Exception e )
		{
			return Fail( $"PackageBuild crashed: {e}" );
		}
	}

	static async Task<int> RunAsync( string[] args )
	{
		var archivePaths = new List<string>();
		var refDirs = new List<string>();
		string outDir = null;

		for ( var i = 0; i < args.Length; i++ )
		{
			switch ( args[i] )
			{
				case "--archive" or "-a" when i + 1 < args.Length:
					archivePaths.Add( args[++i] );
					break;
				case "--out" or "-o" when i + 1 < args.Length:
					outDir = args[++i];
					break;
				case "--refs" or "-r" when i + 1 < args.Length:
					refDirs.Add( args[++i] );
					break;
				default:
					if ( args[i].EndsWith( ".cll", StringComparison.OrdinalIgnoreCase ) )
						archivePaths.Add( args[i] );
					break;
			}
		}

		if ( archivePaths.Count == 0 || string.IsNullOrEmpty( outDir ) )
			return Fail( "Usage: PackageBuild --archive <file.cll> [--archive ...] --out <dir> [--refs <dir> ...]" );

		// Default references to this tool's own directory — in a shipped build that is game/bin/managed, i.e. the
		// exact engine assemblies + toolchain this build ships.
		if ( refDirs.Count == 0 )
			refDirs.Add( AppContext.BaseDirectory );

		var archives = new List<ArchiveEntry>();

		foreach ( var path in archivePaths )
		{
			if ( !File.Exists( path ) )
				return Fail( $"Archive not found: {path}" );

			archives.Add( new ArchiveEntry
			{
				Archive = new CodeArchive( await File.ReadAllBytesAsync( path ) )
			} );
		}

		Console.Error.WriteLine( $"Compiling {archives.Count} archive(s) against {refDirs.Count} reference dir(s)..." );

		var result = await CompileArchivesAsync( archives, refDirs );

		if ( !result.Success )
		{
			foreach ( var error in result.Errors )
				Console.Error.WriteLine( $"  {error}" );

			WriteResult( new CompileResult { Success = false, Errors = result.Errors } );
			return 1;
		}

		Directory.CreateDirectory( outDir );
		var fullOut = Path.GetFullPath( outDir );

		var written = new List<CompiledDll>();

		foreach ( var file in result.Files )
		{
			if ( file.AssemblyData is null )
				continue;

			// AssemblyName comes from the archive's CompilerName (untrusted), so make sure it can't escape --out.
			var dllPath = Path.GetFullPath( Path.Combine( outDir, $"{file.AssemblyName}.dll" ) );
			if ( !dllPath.StartsWith( fullOut + Path.DirectorySeparatorChar, StringComparison.Ordinal ) )
				return Fail( $"Refusing to write outside --out: assembly name '{file.AssemblyName}' resolves to '{dllPath}'." );

			await File.WriteAllBytesAsync( dllPath, file.AssemblyData );

			Console.Error.WriteLine( $"  -> {dllPath} ({file.AssemblyData.Length:N0} bytes)" );
			written.Add( new CompiledDll { AssemblyName = file.AssemblyName, File = dllPath } );
		}

		WriteResult( new CompileResult { Success = true, Dlls = written } );
		return 0;
	}

	static int Fail( string error )
	{
		Console.Error.WriteLine( error );
		WriteResult( new CompileResult { Success = false, Errors = [error] } );
		return 1;
	}

	static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	static void WriteResult( CompileResult result )
		=> Console.Out.WriteLine( JsonSerializer.Serialize( result, JsonOpts ) );

	// --- compile core (low-level CompileGroup path, ported from the sbox.web CLL worker — no engine boot) ---

	static async Task<CompilationOutput> CompileArchivesAsync( List<ArchiveEntry> archives, List<string> refDirs )
	{
		var result = new CompilationOutput();

		using var group = new CompileGroup( "package-build" );
		group.ReferenceProvider = new EngineReferenceProvider( refDirs );
		group.PrintErrorsInConsole = false;
		CompileGroup.SuppressBuildNotifications = false;

		foreach ( var entry in archives )
			group.GetOrCreateCompiler( entry.Archive.CompilerName ).UpdateFromArchive( entry.Archive );

		// BuildAsync handles dependency ordering, parallel compilation, Razor, source generators, blacklist walker, Emit.
		if ( !await group.BuildAsync() )
		{
			result.Errors = group.BuildResult.Diagnostics
				.Where( d => d.Severity >= DiagnosticSeverity.Error )
				.Select( FormatDiagnostic )
				.ToList();

			// A build exception (e.g. a missing reference) isn't a compile diagnostic; don't report success-with-no-output.
			if ( result.Errors.Count == 0 )
				result.Errors.Add( "Build failed with an internal build exception (no compile diagnostics)" );

			return result;
		}

		foreach ( var output in group.BuildResult.Output )
		{
			result.Files.Add( new CompiledFile
			{
				AssemblyName = output.Compiler.AssemblyName,
				AssemblyData = output.AssemblyData
			} );
		}

		return result;
	}

	static string FormatDiagnostic( Diagnostic d )
	{
		var location = d.Location?.SourceTree?.FilePath;
		var line = d.Location?.GetLineSpan().StartLinePosition;
		var loc = location is not null ? $" - {location}:{line}" : "";
		return $"{d.GetMessage()}{loc}";
	}
}

/// <summary>
/// Resolves metadata references from directories of managed DLLs. Enumerates paths up front (cheap) and only
/// builds a <see cref="MetadataReference"/> for names the compiler actually looks up (each tool run is one job,
/// so lazy loading avoids parsing hundreds of unused engine DLLs per compile).
/// </summary>
class EngineReferenceProvider : ICompileReferenceProvider
{
	readonly Dictionary<string, string> _paths = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, PortableExecutableReference> _cache = new( StringComparer.OrdinalIgnoreCase );

	public EngineReferenceProvider( IEnumerable<string> dirs )
	{
		foreach ( var dir in dirs )
		{
			if ( !Directory.Exists( dir ) )
				continue;

			// Recurse (bin/managed lays some DLLs out in subfolders); first name wins.
			foreach ( var dll in Directory.GetFiles( dir, "*.dll", SearchOption.AllDirectories ) )
				_paths.TryAdd( Path.GetFileNameWithoutExtension( dll ), dll );
		}
	}

	public PortableExecutableReference Lookup( string reference )
	{
		if ( _cache.TryGetValue( reference, out var cached ) )
			return cached;

		if ( !_paths.TryGetValue( reference, out var path ) )
			return null;

		try { return _cache[reference] = MetadataReference.CreateFromFile( path ); }
		catch { return _cache[reference] = null; } // skip unreadable / native DLLs
	}
}

class ArchiveEntry
{
	public CodeArchive Archive { get; set; }
}

class CompilationOutput
{
	public List<CompiledFile> Files { get; set; } = [];
	public List<string> Errors { get; set; } = [];
	public bool Success => Errors.Count == 0;
}

class CompiledFile
{
	public string AssemblyName { get; set; }
	public byte[] AssemblyData { get; set; }
}

class CompileResult
{
	public bool Success { get; set; }
	public List<CompiledDll> Dlls { get; set; } = [];
	public List<string> Errors { get; set; } = [];
}

class CompiledDll
{
	public string AssemblyName { get; set; }
	public string File { get; set; }
}
