using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// Compiled shaders on disk, one file per source file. An entry is found by a key of the compiler's identity (this
/// assembly's build and slang.dll's contents), the tier and the source file's path and text, and is used only while
/// every file the compile read still has the contents it had then; anything else compiles again and replaces it.
/// </summary>
internal static class CompileCache
{
	/// <summary>Raised with any change to what an entry holds or how it's keyed.</summary>
	const int Format = 1;

	static readonly JsonSerializerOptions Options = new() { IncludeFields = false, WriteIndented = false };
	static readonly Dictionary<string, string> identities = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The compiler's identity: this assembly's build and the Slang it drives.</summary>
	static string Identity( string slangDirectory )
	{
		lock ( identities )
		{
			if ( identities.TryGetValue( slangDirectory, out var known ) ) return known;
			var slang = Path.Combine( slangDirectory, OperatingSystem.IsWindows() ? "slang.dll" : "libslang.so" );
			var slangHash = File.Exists( slang ) ? Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( slang ) ) ) : "none";
			return identities[slangDirectory] = $"{Format}:{typeof( CompileCache ).Assembly.ManifestModule.ModuleVersionId}:{slangHash}";
		}
	}

	static string Hash( string text ) => Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( text ) ) );

	/// <summary>The file an entry for <paramref name="path"/> lives in, and its key; <paramref name="context"/> is the compiler's search paths.</summary>
	static (string File, string Prefix, string Key) Place( string folder, string context, string slangDirectory, ShaderTier tier, string path, string text )
	{
		var key = Hash( $"{Identity( slangDirectory )}|{context}|{tier}|{path.ToLowerInvariant()}|{text}" );
		var prefix = $"{Path.GetFileNameWithoutExtension( path )}.{Hash( path.ToLowerInvariant() )[..8].ToLowerInvariant()}.";
		return (Path.Combine( folder, prefix + key[..16].ToLowerInvariant() + ".json" ), prefix, key);
	}

	sealed class Entry
	{
		public string Key { get; set; }
		public Dictionary<string, string> Dependencies { get; set; }
		public CompiledShader Shader { get; set; }
		public List<CompiledStage> Stages { get; set; }
		public Dictionary<string, int[]> Passes { get; set; }
	}

	/// <summary>The cached compile of <paramref name="path"/>, or null.</summary>
	public static CompiledShader Load( string folder, string context, string slangDirectory, ShaderTier tier, string path, string text )
	{
		var (file, _, key) = Place( folder, context, slangDirectory, tier, path, text );
		if ( !File.Exists( file ) ) return null;

		try
		{
			var clock = System.Diagnostics.Stopwatch.StartNew();
			var entry = JsonSerializer.Deserialize<Entry>( File.ReadAllText( file ), Options );
			if ( entry?.Key != key || entry.Shader is null || entry.Stages is null || entry.Passes is null ) return null;

			// Every file it read must read the same now
			foreach ( var (dependency, hash) in entry.Dependencies )
			{
				if ( !File.Exists( dependency ) || Hash( File.ReadAllText( dependency ) ) != hash ) return null;
			}

			var passes = new Dictionary<string, CompiledPass>();
			foreach ( var (name, stages) in entry.Passes )
				passes[name] = new CompiledPass { Name = name, Vertex = stages[0] >= 0 ? entry.Stages[stages[0]] : null, Pixel = stages[1] >= 0 ? entry.Stages[stages[1]] : null };

			var shader = entry.Shader;
			return new CompiledShader
			{
				Name = shader.Name,
				Tier = shader.Tier,
				Path = path,
				Material = shader.Material,
				Passes = passes,
				Generated = shader.Generated,
				GpuDrivenError = shader.GpuDrivenError,
				Dependencies = shader.Dependencies,
				Milliseconds = clock.Elapsed.TotalMilliseconds,
				FromCache = true,
			};
		}
		catch ( Exception e ) when ( e is JsonException or IOException or NotSupportedException or InvalidOperationException or IndexOutOfRangeException )
		{
			return null;
		}
	}

	/// <summary>Keep <paramref name="shader"/>, compiled from <paramref name="text"/>, replacing older entries for its file.</summary>
	public static void Save( string folder, string context, string slangDirectory, string path, string text, CompiledShader shader )
	{
		var (file, prefix, key) = Place( folder, context, slangDirectory, shader.Tier, path, text );
		try
		{
			// Stages shared between passes stay shared
			var stages = new List<CompiledStage>();
			int Index( CompiledStage stage )
			{
				if ( stage is null ) return -1;
				var at = stages.IndexOf( stage );
				if ( at < 0 )
				{
					at = stages.Count;
					stages.Add( stage );
				}

				return at;
			}

			var entry = new Entry
			{
				Key = key,
				Dependencies = shader.Dependencies.ToDictionary( d => d, d => Hash( File.ReadAllText( d ) ), StringComparer.OrdinalIgnoreCase ),
				Passes = shader.Passes.ToDictionary( p => p.Key, p => new[] { Index( p.Value.Vertex ), Index( p.Value.Pixel ) } ),
				Stages = stages,
				Shader = new CompiledShader
				{
					Name = shader.Name,
					Tier = shader.Tier,
					Path = shader.Path,
					Material = shader.Material,
					Passes = new Dictionary<string, CompiledPass>(),
					Generated = shader.Generated,
					GpuDrivenError = shader.GpuDrivenError,
					Dependencies = shader.Dependencies,
				},
			};

			Directory.CreateDirectory( folder );
			var temporary = file + ".tmp";
			File.WriteAllText( temporary, JsonSerializer.Serialize( entry, Options ) );
			File.Move( temporary, file, overwrite: true );

			foreach ( var old in Directory.GetFiles( folder, prefix + "*.json" ) )
			{
				if ( !string.Equals( old, file, StringComparison.OrdinalIgnoreCase ) ) File.Delete( old );
			}
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			// A cache that can't be written just compiles again next time
		}
	}
}
