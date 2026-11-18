using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Sandbox.ShaderCompiler.Slang;

namespace Sandbox.ShaderCompiler;

public sealed partial class SlangCompiler
{
	/// <summary>
	/// Compile one stage of a pass module (<see cref="PassModule"/>) with these combo values (and any other defines, such
	/// as a mode's combos): its entry point (<c>MainVs</c>, <c>MainPs</c>, <c>MainCs</c>, or <c>MainTs</c> and <c>MainMs</c>)
	/// with the values as defines, as vfx compiles a combo. Pixel resources go in set 1, as for every other shader; task
	/// and mesh resources in set 0, as the vertex stage's. A mesh stage's positions aren't flipped as a vertex stage's
	/// are: it negates y itself. Kept in <see cref="CacheFolder"/> per stage and values. Throws
	/// <see cref="ShaderCompileException"/> on errors.
	/// </summary>
	public CompiledPassStage CompilePass( PassModule module, StageKind stage, IReadOnlyDictionary<string, int> values ) => Wait( CompilePassAsync( module, stage, values ) );

	/// <summary>
	/// <see cref="CompilePass"/> on a compile thread: from the cache at once when it's there. <paramref name="priority"/>
	/// (null: urgent) orders it among the other compiles.
	/// </summary>
	public Task<CompiledPassStage> CompilePassAsync( PassModule module, StageKind stage, IReadOnlyDictionary<string, int> values, CompilePriority priority = null )
	{
		var defines = new SortedDictionary<string, string>( StringComparer.Ordinal );
		foreach ( var (name, value) in values ) defines[name] = value.ToString( System.Globalization.CultureInfo.InvariantCulture );

		var key = $"{stage}|{Path.GetFileName( module.Path ).ToLowerInvariant()}|{string.Join( ';', defines.Select( d => $"{d.Key}={d.Value}" ) )}";
		using ( Zone( CompilePhase.CacheLoad ) )
		{
			if ( PassCache.Load( CacheFolder, Offline( module.Path ), Paths( module.Path ), slangDirectory, key, module ) is { } cached )
			{
				System.Threading.Interlocked.Increment( ref cachedStages );
				if ( saved is { } collect && PassCache.Entry( Paths( module.Path ), slangDirectory, key, module, cached ) is var (entryKey, bytes, dependencies) ) collect( entryKey, bytes, dependencies );
				return Task.FromResult( cached );
			}
		}

		Compiling?.Invoke( $"{Path.GetFileName( module.Path )} {stage} {string.Join( ' ', defines.Select( d => $"{d.Key}={d.Value}" ) )}" );

		return SlangWorkers.Run( () => CompilePassStage( module, stage, values, defines, key ), priority );
	}

	CompiledPassStage CompilePassStage( PassModule module, StageKind stage, IReadOnlyDictionary<string, int> values, IReadOnlyDictionary<string, string> defines, string key )
	{
		using var zone = Zone( CompilePhase.PassStage );
		var clock = Stopwatch.StartNew();
		var (compiled, dependencies) = CompilePassNow( module, stage, values, defines );
		var result = new CompiledPassStage
		{
			Stage = compiled,
			ThreadGroupSize = stage is StageKind.Compute or StageKind.Task or StageKind.Mesh ? LocalSize( compiled.Spirv ) : (0, 0, 0),
			Dependencies = dependencies,
			Milliseconds = clock.Elapsed.TotalMilliseconds,
		};

		if ( CacheFolder is not null || saved is not null )
		{
			using ( Zone( CompilePhase.CacheSave ) ) PassCache.Save( CacheFolder, Paths( module.Path ), slangDirectory, key, module, result, saved );
		}

		return result;
	}

	(CompiledStage, List<string>) CompilePassNow( PassModule module, StageKind stage, IReadOnlyDictionary<string, int> values, IReadOnlyDictionary<string, string> defines )
	{
		using var zone = Zone( CompilePhase.Stage );
		System.Threading.Interlocked.Increment( ref stages );
		var directory = Path.GetDirectoryName( module.Path );
		var read = new ReadFiles( module.Path );
		using var request = Request( stage, [directory], read, defines );
		var source = VfxIncludes.Rewrite( module.Text, request.Annotations );

		// A global declared in several #if branches (a texture read as sRGB with one combo) takes the active branch's annotations
		VfxIncludes.Rewrite( module.ActiveText( stage, values ), request.Annotations, module.Text );

		request.AddSource( module.Path, source );
		var entry = PassModule.Entry( stage );
		request.AddEntryPoint( entry, stage );
		request.Compile( module.Path );

		var spirv = request.Code() ?? throw new ShaderCompileException( module.Path, $"{entry}: Slang returned no code. {request.Diagnostics}" );
		using ( Zone( CompilePhase.Optimize ) ) spirv = SpirvOptimizer.Optimize( slangDirectory, spirv, out _ );
		if ( stage == StageKind.Pixel ) spirv = Spirv.MoveSet( spirv, 0, 1 );
		CompiledStage compiled;
		using ( Zone( CompilePhase.Describe ) ) compiled = StageReflection.Describe( stage, entry, spirv, request.ReflectionJson(), request.Annotations );
		return (AppendBuffers( compiled, request.Annotations ), read.Sorted());
	}

	/// <summary>
	/// An append or consume buffer is two bindings in SPIR-V, <c>X.elements</c> and <c>X.counter</c>: the elements bind as
	/// <c>X</c> did, with its annotations; the counter keeps its name, which says whose it is, and no annotations.
	/// </summary>
	static CompiledStage AppendBuffers( CompiledStage stage, IReadOnlyDictionary<string, Annotations> annotations )
	{
		if ( !stage.Bindings.Any( b => b.Variable.EndsWith( ".elements", StringComparison.Ordinal ) || b.Variable.EndsWith( ".counter", StringComparison.Ordinal ) ) ) return stage;

		var bindings = stage.Bindings.Select( b =>
		{
			if ( b.Variable.EndsWith( ".elements", StringComparison.Ordinal ) )
			{
				var name = b.Variable[..^".elements".Length];
				return b with { Variable = name, Annotations = annotations.GetValueOrDefault( name ) };
			}

			return b.Variable.EndsWith( ".counter", StringComparison.Ordinal ) ? b with { Annotations = null } : b;
		} ).ToList();

		return new CompiledStage
		{
			Stage = stage.Stage,
			Entry = stage.Entry,
			Spirv = stage.Spirv,
			Bindings = bindings,
			Globals = stage.Globals,
			Inputs = stage.Inputs,
			Specializations = stage.Specializations,
		};
	}

	/// <summary>A compute entry's thread group size (<c>OpExecutionMode LocalSize</c>).</summary>
	static (int X, int Y, int Z) LocalSize( byte[] spirv )
	{
		var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>( spirv.AsSpan() );
		for ( int i = 5; i < words.Length; )
		{
			var count = (int)(words[i] >> 16);
			var opcode = words[i] & 0xFFFF;
			if ( count == 0 ) break;
			// OpExecutionMode %entry LocalSize x y z
			if ( opcode == 16 && count >= 6 && words[i + 2] == 17 ) return ((int)words[i + 3], (int)words[i + 4], (int)words[i + 5]);
			i += count;
		}

		return (1, 1, 1);
	}
}

/// <summary>One stage of a pass module, compiled for some combo values.</summary>
public sealed class CompiledPassStage
{
	/// <summary>The stage: SPIR-V, bindings, <c>$Globals</c>, vertex inputs, specialization constants.</summary>
	public CompiledStage Stage { get; init; }

	/// <summary>Thread group size of a compute stage; zeros otherwise.</summary>
	public (int X, int Y, int Z) ThreadGroupSize { get; init; }

	/// <summary>Every file the compile read.</summary>
	public IReadOnlyList<string> Dependencies { get; init; } = [];

	/// <summary>Compile time, or the cache lookup's.</summary>
	public double Milliseconds { get; init; }

	/// <summary>Whether it came from the compiler's disk cache.</summary>
	public bool FromCache { get; init; }

	/// <summary>Whether it came from its module's offline compile (<see cref="SlangOutput"/>), which build-shaders makes.</summary>
	public bool Offline { get; init; }
}

/// <summary>
/// Compiled pass stages on disk, one file per stage and combo values: used while the compiler, the module's text and
/// every file the compile read are as they were.
/// </summary>
internal static class PassCache
{
	const int Format = 3;
	static readonly Dictionary<string, (DateTime Time, long Length, string Hash, long CheckedAt, int Generation)> fileHashes = new( StringComparer.OrdinalIgnoreCase );
	static int generation;

	/// <summary>
	/// A file's hash, read again only when its time or length changed; null when it's gone. Its time is looked at again at
	/// most every <see cref="PassModule.IncludeCheckMs"/>, and at once after <see cref="Recheck"/>: each stage a variant loads checks
	/// every file its compile read, and a map's first frame asked the file system thousands of times.
	/// </summary>
	internal static string FileHash( string path )
	{
		var now = Environment.TickCount64;
		lock ( fileHashes )
		{
			if ( fileHashes.TryGetValue( path, out var known ) && KeepLooks && known.Generation == generation && now - known.CheckedAt < PassModule.IncludeCheckMs ) return known.Hash;
		}

		System.Threading.Interlocked.Increment( ref Looks );
		var info = new FileInfo( path );
		if ( !info.Exists )
		{
			lock ( fileHashes ) fileHashes.Remove( path );
			return null;
		}

		lock ( fileHashes )
		{
			if ( fileHashes.TryGetValue( path, out var known ) && known.Time == info.LastWriteTimeUtc && known.Length == info.Length )
			{
				fileHashes[path] = known with { CheckedAt = now, Generation = generation };
				return known.Hash;
			}
		}

		var hash = CompileCache.Hash( File.ReadAllText( path ) );
		lock ( fileHashes ) fileHashes[path] = (info.LastWriteTimeUtc, info.Length, hash, now, generation);
		return hash;
	}

	/// <summary>Whether <see cref="FileHash"/> trusts its last look for a while (on); off asks each time, for comparisons.</summary>
	internal static bool KeepLooks { get; set; } = true;

	/// <summary>Times <see cref="FileHash"/> asked the file system, for tests.</summary>
	internal static int Looks;

	/// <summary>A watched file changed: the next <see cref="FileHash"/> of each file looks at it again.</summary>
	internal static void Recheck()
	{
		lock ( fileHashes ) generation++;
	}

	/// <summary>An entry's key: the compiler, the stage, module file name and values (<paramref name="key"/>), and the module's text.</summary>
	static string Key( string slangDirectory, string key, PassModule module ) => CompileCache.Hash( $"{Format}|{CompileCache.Identity( slangDirectory )}|{key}|{module.Text}" );

	static string Place( string folder, string key, PassModule module ) =>
		Path.Combine( folder, $"{Path.GetFileNameWithoutExtension( module.Path )}.{CompileCache.Hash( $"{Path.GetFullPath( module.Path ).ToLowerInvariant()}|{key}" )[..16].ToLowerInvariant()}.bin" );

	public static CompiledPassStage Load( string folder, IReadOnlyList<SlangOutput> offline, DependencyPaths paths, string slangDirectory, string key, PassModule module )
	{
		var full = Key( slangDirectory, key, module );
		CompiledPassStage Read( ReadOnlySpan<byte> bytes, bool fromOffline ) => ShaderCacheFile.TryRead( bytes, fromOffline ? "" : full, paths.Resolve, ( r, dependencies ) => ShaderBinary.ReadPassStage( r, dependencies, fromOffline ), out var stage ) ? stage : null;
		foreach ( var output in offline )
		{
			if ( output.TryGet( full, out var bytes ) && Read( bytes.Span, true ) is { } compiled ) return compiled;
		}

		if ( folder is not null )
		{
			var file = Place( folder, key, module );
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

	/// <summary>An entry for <paramref name="stage"/>: its key, bytes and the files it read as named there, or null when it read a file <paramref name="paths"/> can't name.</summary>
	public static (string Key, byte[] Bytes, List<(string Name, string Hash)> Dependencies)? Entry( DependencyPaths paths, string slangDirectory, string key, PassModule module, CompiledPassStage stage )
	{
		var dependencies = new List<(string, string)>();
		foreach ( var dependency in stage.Dependencies )
		{
			if ( paths.Name( dependency ) is not { } name ) return null;
			dependencies.Add( (name, FileHash( dependency )) );
		}

		var full = Key( slangDirectory, key, module );
		return (full, ShaderCacheFile.Write( full, dependencies, w => ShaderBinary.WritePassStage( w, stage ) ), dependencies);
	}

	public static void Save( string folder, DependencyPaths paths, string slangDirectory, string key, PassModule module, CompiledPassStage stage, Action<string, byte[], List<(string Name, string Hash)>> saved = null )
	{
		if ( Entry( paths, slangDirectory, key, module, stage ) is not var (full, bytes, dependencies) ) return;
		saved?.Invoke( full, bytes, dependencies );
		if ( folder is not null ) ShaderCacheFile.Save( Place( folder, key, module ), bytes );
	}
}
