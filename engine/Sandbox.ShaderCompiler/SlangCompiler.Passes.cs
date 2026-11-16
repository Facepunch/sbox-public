using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
	public CompiledPassStage CompilePass( PassModule module, StageKind stage, IReadOnlyDictionary<string, int> values )
	{
		var defines = new SortedDictionary<string, string>( StringComparer.Ordinal );
		foreach ( var (name, value) in values ) defines[name] = value.ToString( System.Globalization.CultureInfo.InvariantCulture );

		var key = $"{slangDirectory}|{string.Join( ';', searchPaths )}|{stage}|{module.Path.ToLowerInvariant()}|{string.Join( ';', defines.Select( d => $"{d.Key}={d.Value}" ) )}";
		if ( CacheFolder is not null && PassCache.Load( CacheFolder, slangDirectory, key, module ) is { } cached ) return cached;

		var clock = Stopwatch.StartNew();
		var directory = Path.GetDirectoryName( module.Path );
		CompiledStage compiled;
		List<string> dependencies;
		lock ( compileLock )
		{
			readFiles = new( StringComparer.OrdinalIgnoreCase ) { module.Path };
			using var request = Request( stage, [directory], defines );
			var source = VfxIncludes.Rewrite( module.Text, request.Annotations );

			// A global declared in several #if branches (a texture read as sRGB with one combo) takes the active branch's annotations
			VfxIncludes.Rewrite( module.ActiveText( stage, values ), request.Annotations );

			request.AddSource( module.Path, source );
			var entry = PassModule.Entry( stage );
			request.AddEntryPoint( entry, stage );
			request.Compile( module.Path );

			var spirv = request.Code() ?? throw new ShaderCompileException( module.Path, $"{entry}: Slang returned no code. {request.Diagnostics}" );
			spirv = SpirvOptimizer.Optimize( slangDirectory, spirv, out _ );
			if ( stage == StageKind.Pixel ) spirv = Spirv.MoveSet( spirv, 0, 1 );
			compiled = StageReflection.Describe( stage, entry, spirv, request.ReflectionJson(), request.Annotations );
			compiled = AppendBuffers( compiled, request.Annotations );
			dependencies = readFiles.Order( StringComparer.OrdinalIgnoreCase ).ToList();
		}

		var result = new CompiledPassStage
		{
			Stage = compiled,
			ThreadGroupSize = stage is StageKind.Compute or StageKind.Task or StageKind.Mesh ? LocalSize( compiled.Spirv ) : (0, 0, 0),
			Dependencies = dependencies,
			Milliseconds = clock.Elapsed.TotalMilliseconds,
		};

		if ( CacheFolder is not null ) PassCache.Save( CacheFolder, slangDirectory, key, module, result );
		return result;
	}

	/// <summary>
	/// An append or consume buffer is two bindings in SPIR-V, <c>X.elements</c> and <c>X.counter</c>: the elements bind as
	/// <c>X</c> did, with its annotations; the counter is named nothing, as vfx's tables leave it.
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

			return b.Variable.EndsWith( ".counter", StringComparison.Ordinal ) ? b with { Variable = "", Annotations = null } : b;
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
}

/// <summary>
/// Compiled pass stages on disk, one file per stage and combo values: used while the compiler, the module's text and
/// every file the compile read are as they were.
/// </summary>
internal static class PassCache
{
	const int Format = 1;
	static readonly JsonSerializerOptions Options = new() { IncludeFields = false, WriteIndented = false };
	static readonly Dictionary<string, string> identities = new( StringComparer.OrdinalIgnoreCase );
	static readonly Dictionary<string, (DateTime Time, long Length, string Hash)> fileHashes = new( StringComparer.OrdinalIgnoreCase );

	sealed class Entry
	{
		public string Key { get; set; }
		public Dictionary<string, string> Dependencies { get; set; }
		public CompiledStage Stage { get; set; }
		public int[] Threads { get; set; }
	}

	static string Hash( string text ) => Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( text ) ) );

	static string Identity( string slangDirectory )
	{
		lock ( identities )
		{
			if ( identities.TryGetValue( slangDirectory, out var known ) ) return known;
			var slang = Path.Combine( slangDirectory, OperatingSystem.IsWindows() ? "slang.dll" : "libslang.so" );
			var slangHash = File.Exists( slang ) ? Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( slang ) ) ) : "none";
			return identities[slangDirectory] = $"{Format}:{typeof( PassCache ).Assembly.ManifestModule.ModuleVersionId}:{slangHash}:{SpirvOptimizer.Identity( slangDirectory )}";
		}
	}

	/// <summary>A file's hash, read again only when its time or length changed.</summary>
	static string FileHash( string path )
	{
		var info = new FileInfo( path );
		if ( !info.Exists ) return null;
		lock ( fileHashes )
		{
			if ( fileHashes.TryGetValue( path, out var known ) && known.Time == info.LastWriteTimeUtc && known.Length == info.Length ) return known.Hash;
		}

		var hash = Hash( File.ReadAllText( path ) );
		lock ( fileHashes ) fileHashes[path] = (info.LastWriteTimeUtc, info.Length, hash);
		return hash;
	}

	static (string File, string Key) Place( string folder, string slangDirectory, string key, PassModule module )
	{
		var full = Hash( $"{Identity( slangDirectory )}|{key}|{module.Text}" );
		return (Path.Combine( folder, $"{Path.GetFileNameWithoutExtension( module.Path )}.{Hash( key )[..16].ToLowerInvariant()}.json" ), full);
	}

	public static CompiledPassStage Load( string folder, string slangDirectory, string key, PassModule module )
	{
		var (file, full) = Place( folder, slangDirectory, key, module );
		if ( !File.Exists( file ) ) return null;
		try
		{
			var clock = Stopwatch.StartNew();
			var entry = JsonSerializer.Deserialize<Entry>( File.ReadAllText( file ), Options );
			if ( entry?.Key != full || entry.Stage is null || entry.Threads is not { Length: 3 } ) return null;
			foreach ( var (dependency, hash) in entry.Dependencies )
			{
				if ( FileHash( dependency ) != hash ) return null;
			}

			return new CompiledPassStage
			{
				Stage = entry.Stage,
				ThreadGroupSize = (entry.Threads[0], entry.Threads[1], entry.Threads[2]),
				Dependencies = [.. entry.Dependencies.Keys],
				Milliseconds = clock.Elapsed.TotalMilliseconds,
				FromCache = true,
			};
		}
		catch ( Exception e ) when ( e is JsonException or IOException or NotSupportedException or InvalidOperationException )
		{
			return null;
		}
	}

	public static void Save( string folder, string slangDirectory, string key, PassModule module, CompiledPassStage stage )
	{
		var (file, full) = Place( folder, slangDirectory, key, module );
		try
		{
			var entry = new Entry
			{
				Key = full,
				Dependencies = stage.Dependencies.ToDictionary( d => d, FileHash, StringComparer.OrdinalIgnoreCase ),
				Stage = stage.Stage,
				Threads = [stage.ThreadGroupSize.X, stage.ThreadGroupSize.Y, stage.ThreadGroupSize.Z],
			};

			Directory.CreateDirectory( folder );
			var temporary = file + ".tmp";
			File.WriteAllText( temporary, JsonSerializer.Serialize( entry, Options ) );
			File.Move( temporary, file, overwrite: true );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
		}
	}
}
