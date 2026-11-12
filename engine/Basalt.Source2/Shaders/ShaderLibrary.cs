using Basalt.Gpu;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Basalt.Source2.Shaders;

/// <summary>
/// s&amp;box's compiled shaders on a <see cref="Device"/>: loads <c>.shader_c</c> files by path, picks variants, and
/// owns the shader modules and binding layouts made for them (one module per distinct SPIR-V, one layout per distinct
/// set). Thread-safe.
/// </summary>
public sealed class ShaderLibrary : IDisposable
{
	readonly Device gpu;
	readonly Dictionary<string, ShaderFile> files = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<byte[], ShaderHandle> modules = new( ReferenceEqualityComparer.Instance );
	readonly ConditionalWeakTable<byte[], SpirvReflection> reflections = new();
	readonly Dictionary<string, BindingLayoutHandle> layouts = new();

	/// <summary>Where shaders are found.</summary>
	public ContentFiles Files { get; }

	/// <summary>The device modules and layouts are made on.</summary>
	public Device Gpu => gpu;

	/// <summary>Load shaders from <paramref name="files"/> onto <paramref name="gpu"/>.</summary>
	public ShaderLibrary( Device gpu, ContentFiles files )
	{
		this.gpu = gpu;
		Files = files;
	}

	/// <summary>
	/// A shader by path (<c>shaders/complex.shader</c>, with or without <c>_c</c>), loaded once; null if there's no
	/// compiled file for it.
	/// </summary>
	public ShaderFile Load( string path )
	{
		var key = path.Replace( '\\', '/' );
		if ( key.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) key = key[..^2];
		lock ( files )
		{
			if ( files.TryGetValue( key, out var cached ) ) return cached;
		}

		var full = Files.Find( key );
		if ( full is null ) return null;
		var shader = ShaderFile.Load( full );
		lock ( files ) files[key] = shader;
		return shader;
	}

	/// <summary>
	/// A GPU-driven variant's material textures are texture variables at this register plus their word in the material
	/// record (<c>BASALT_GPU_DRIVEN_TEXTURE_REGISTER</c>, <c>src/vfx/vfx_vulkan/basalt_gpu_driven.h</c>).
	/// </summary>
	public const int GpuDrivenTextureRegister = 1024;

	/// <summary>
	/// The GPU-driven variant's form: what vfx_vulkan's rewrite (<c>src/vfx/vfx_vulkan/basalt_gpu_driven.cpp</c>) makes
	/// of a shader. Raise it with any change there, so variants compiled at load and cached are compiled again.
	/// </summary>
	public const int GpuDrivenVersion = 3;

	readonly Dictionary<ShaderFile, ShaderFile> gpuDriven = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// Compiles a shader's GPU-driven variant when there's none beside it (a package's shader): given the shader's
	/// compiled file and where to write the variant, true once written. Runs on a worker thread. Null compiles nothing.
	/// </summary>
	public Func<string, string, bool> GpuDrivenCompiler { get; set; }

	/// <summary>
	/// The folder <see cref="GpuDrivenCompiler"/>'s variants are cached in, by shader name, the shader file's hash and
	/// <see cref="GpuDrivenCompilerVersion"/>.
	/// </summary>
	public string GpuDrivenCache { get; set; }

	/// <summary>What the cached variants were compiled by: a different version compiles them again.</summary>
	public string GpuDrivenCompilerVersion { get; set; } = "";

	string GpuDrivenVersionTag => Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( GpuDrivenCompilerVersion ) ), 0, 4 ).ToLowerInvariant();

	/// <summary>Delete <see cref="GpuDrivenCache"/>'s variants (and failure markers) from other compiler versions.</summary>
	public void PruneGpuDrivenCache()
	{
		if ( string.IsNullOrEmpty( GpuDrivenCache ) || !Directory.Exists( GpuDrivenCache ) ) return;

		var current = $".{GpuDrivenVersionTag}.gpudriven.shader_c";
		foreach ( var file in Directory.EnumerateFiles( GpuDrivenCache, "*.gpudriven.shader_c*" ) )
		{
			if ( file.Contains( current, StringComparison.OrdinalIgnoreCase ) ) continue;
			try
			{
				File.Delete( file );
			}
			catch ( IOException )
			{
				// Another process has it open: next time
			}
			catch ( UnauthorizedAccessException )
			{
			}
		}
	}

	readonly Dictionary<ShaderFile, string> gpuDrivenCachePaths = new( ReferenceEqualityComparer.Instance );
	readonly HashSet<ShaderFile> gpuDrivenPending = new( ReferenceEqualityComparer.Instance );
	readonly HashSet<ShaderFile> gpuDrivenCompiled = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// A shader's GPU-driven variant (<c>ShaderCompiler -gpudriven</c>): <c>name.gpudriven.shader_c</c> beside its
	/// <c>.shader_c</c>, or one <see cref="GpuDrivenCompiler"/> made, loaded once; null when there's none. Without one
	/// beside it, the first call starts compiling it and returns null until it's ready (<see cref="GpuDrivenPending"/>).
	/// Same modes, features and combos as the shader.
	/// </summary>
	public ShaderFile GpuDriven( ShaderFile shader )
	{
		if ( shader is null ) return null;
		lock ( gpuDriven )
		{
			if ( gpuDriven.TryGetValue( shader, out var cached ) ) return cached;
		}

		ShaderFile variant = null;
		const string suffix = ".shader_c";
		if ( shader.Name.EndsWith( suffix, StringComparison.OrdinalIgnoreCase ) )
		{
			var path = shader.Name[..^suffix.Length] + ".gpudriven" + suffix;
			if ( File.Exists( path ) )
			{
				variant = ShaderFile.Load( path );
			}
			else
			{
				if ( !CompiledGpuDriven( shader, out var compiled ) ) return null;
				if ( compiled is not null ) variant = ShaderFile.Load( compiled );
			}
		}

		lock ( gpuDriven ) gpuDriven[shader] = variant;
		return variant;
	}

	/// <summary>Whether <paramref name="shader"/>'s GPU-driven variant is being compiled, so a null one isn't final yet.</summary>
	public bool GpuDrivenPending( ShaderFile shader )
	{
		lock ( gpuDrivenPending ) return shader is not null && gpuDrivenPending.Contains( shader );
	}

	/// <summary>
	/// The cached compiled variant of a shader with none beside it, in <paramref name="path"/> (null when it can't be
	/// had); false while it's compiling.
	/// </summary>
	bool CompiledGpuDriven( ShaderFile shader, out string path )
	{
		path = null;
		if ( GpuDrivenCompiler is not { } compiler || string.IsNullOrEmpty( GpuDrivenCache ) || !File.Exists( shader.Name ) ) return true;

		lock ( gpuDrivenPending )
		{
			// Pending until a lookup sees it finished, so nothing settles on null in between
			if ( gpuDrivenPending.Contains( shader ) )
			{
				if ( !gpuDrivenCompiled.Remove( shader ) ) return false;
				gpuDrivenPending.Remove( shader );
			}
			if ( !gpuDrivenCachePaths.TryGetValue( shader, out path ) )
			{
				// By name and contents, so a changed package shader compiles again
				var hash = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( File.ReadAllBytes( shader.Name ) ), 0, 8 );
				var version = GpuDrivenVersionTag;
				var name = System.Text.RegularExpressions.Regex.Replace( Path.GetFileNameWithoutExtension( shader.Name ), "[^a-zA-Z0-9_]", "_" );
				path = Path.Combine( GpuDrivenCache, $"{name}.{hash}.{version}.gpudriven.shader_c" ).ToLowerInvariant();
				gpuDrivenCachePaths[shader] = path;
			}

			if ( File.Exists( path ) ) return true;

			// Failed with this compiler before: classic it stays
			if ( File.Exists( path + ".failed" ) )
			{
				path = null;
				return true;
			}

			gpuDrivenPending.Add( shader );
		}

		var source = shader.Name;
		var output = path;
		Task.Run( () =>
		{
			var written = false;
			try
			{
				written = compiler( source, output ) && File.Exists( output );
			}
			catch ( Exception )
			{
				written = false;
			}

			if ( !written )
			{
				try
				{
					Directory.CreateDirectory( Path.GetDirectoryName( output ) );
					File.WriteAllText( output + ".failed", GpuDrivenCompilerVersion );
				}
				catch ( IOException )
				{
				}
			}

			lock ( gpuDrivenPending ) gpuDrivenCompiled.Add( shader );
		} );

		path = null;
		return false;
	}

	/// <summary>
	/// The variant of <paramref name="shader"/> that draws <paramref name="mode"/> for a material with these feature
	/// values (by feature index; see <see cref="ShaderFile.FeatureIndex"/>), with dynamic combos from
	/// <paramref name="dynamicCombos"/> and specialization attributes from <paramref name="attributes"/>. Null when the
	/// shader has no such mode. Throws if the combo was never compiled. With <paramref name="gpuDriven"/>, from the
	/// shader's GPU-driven file (a mode's fallback shader's too); null when there's none.
	/// </summary>
	public ShaderVariant Variant( ShaderFile shader, string mode, ReadOnlySpan<int> features, Func<string, int?> dynamicCombos = null, Func<string, int?> attributes = null, bool gpuDriven = false )
	{
		var drawMode = shader.Mode( mode );
		if ( drawMode is null ) return null;
		if ( !string.IsNullOrEmpty( drawMode.Fallback ) )
		{
			var fallback = Load( drawMode.Fallback.Contains( '/' ) ? drawMode.Fallback : $"shaders/{drawMode.Fallback}" );
			return fallback is null ? null : Variant( fallback, mode, [], dynamicCombos, attributes, gpuDriven );
		}

		if ( gpuDriven )
		{
			shader = GpuDriven( shader );
			if ( shader is null ) return null;
			drawMode = shader.Mode( mode );
			if ( drawMode is null ) return null;
		}

		ShaderStage Stage( ShaderProgramType type, ReadOnlySpan<int> features )
		{
			var program = shader[type];
			if ( program is null ) return null;

			var staticId = shader.StaticComboId( program, drawMode, features );
			if ( !program.HasCombo( staticId ) ) throw new InvalidDataException( $"{shader.Name}: {type} static combo {staticId} for mode {mode} wasn't compiled (skipped by its rules)" );
			var combo = program.LoadCombo( staticId );

			var dynamicId = ShaderFile.DynamicComboId( program, dynamicCombos, features );
			var dynamic = combo.Dynamic( dynamicId ) ?? combo.Dynamic( 0 ) ?? throw new InvalidDataException( $"{shader.Name}: {type} has no dynamic combo {dynamicId}" );
			return new ShaderStage( this, type, program, combo, dynamic, attributes );
		}

		var vertex = Stage( ShaderProgramType.Vertex, features );
		var pixel = Stage( ShaderProgramType.Pixel, features );
		var compute = Stage( ShaderProgramType.Compute, features );

		// Materials that resolve to the same combos and constants share one variant, so they share its layouts and
		// pipelines, and draws of them one after another don't switch pipeline. A GPU-driven variant (its combos are the
		// GPU-driven file's) holds nothing of a material's and is always shared: its draws share a multi-draw
		if ( !ShareVariants && !gpuDriven ) return new ShaderVariant( this, shader, drawMode, vertex, pixel, compute );
		var key = new VariantKey( drawMode, vertex?.Dynamic, pixel?.Dynamic, compute?.Dynamic, ConstantsKey( vertex, pixel, compute ) );
		lock ( variants )
		{
			if ( variants.TryGetValue( key, out var shared ) ) return shared;
			var variant = new ShaderVariant( this, shader, drawMode, vertex, pixel, compute );
			variants[key] = variant;
			return variant;
		}
	}

	readonly record struct VariantKey( ShaderMode Mode, DynamicCombo Vertex, DynamicCombo Pixel, DynamicCombo Compute, string Constants );

	readonly Dictionary<VariantKey, ShaderVariant> variants = new();

	/// <summary>Whether materials that resolve to the same variant share it (on); off, each asks for its own. For comparisons.</summary>
	public bool ShareVariants { get; set; } = true;

	/// <summary>Variants made: one per distinct mode, combos and specialization constants, however many materials draw it.</summary>
	public int VariantCount
	{
		get
		{
			lock ( variants ) return variants.Count;
		}
	}

	static string ConstantsKey( ShaderStage vertex, ShaderStage pixel, ShaderStage compute )
	{
		var key = new System.Text.StringBuilder();
		foreach ( var stage in (ReadOnlySpan<ShaderStage>)[vertex, pixel, compute] )
		{
			key.Append( '|' );
			if ( stage is null ) continue;
			foreach ( var constant in stage.Constants ) key.Append( constant.Id ).Append( '=' ).Append( constant.Value ).Append( ',' );
		}

		return key.ToString();
	}

	/// <summary>The device module for a shader's SPIR-V, made once per distinct module.</summary>
	internal ShaderHandle Module( ShaderBytecode bytecode, string name )
	{
		var spirv = bytecode.Shared.Spirv;
		lock ( modules )
		{
			if ( modules.TryGetValue( spirv, out var module ) ) return module;
			module = gpu.CreateShader( spirv, name );
			modules[spirv] = module;
			return module;
		}
	}

	/// <summary>A shader's reflection, made once per distinct module.</summary>
	internal SpirvReflection Reflect( ShaderBytecode bytecode ) => reflections.GetValue( bytecode.Shared.Spirv, spirv => SpirvReflection.Read( spirv ) );

	/// <summary>A binding layout for these slots, shared by every variant that has the same set.</summary>
	internal BindingLayoutHandle Layout( List<BindingSlot> slots )
	{
		var key = string.Join( ';', slots.ConvertAll( s => $"{s.Binding},{(int)s.Type},{(int)s.Stages},{s.Count}" ) );
		lock ( layouts )
		{
			if ( layouts.TryGetValue( key, out var layout ) ) return layout;
			layout = gpu.CreateBindingLayout( [.. slots] );
			layouts[key] = layout;
			return layout;
		}
	}

	/// <summary>Destroy every module and layout made here.</summary>
	public void Dispose()
	{
		lock ( modules )
		{
			foreach ( var module in modules.Values ) gpu.Destroy( module );
			modules.Clear();
		}

		lock ( layouts )
		{
			foreach ( var layout in layouts.Values ) gpu.Destroy( layout );
			layouts.Clear();
		}
	}
}
