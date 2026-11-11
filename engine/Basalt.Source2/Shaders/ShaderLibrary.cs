using Basalt.Gpu;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

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

	readonly Dictionary<ShaderFile, ShaderFile> gpuDriven = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// A shader's GPU-driven variant (<c>ShaderCompiler -gpudriven</c>): <c>name.gpudriven.shader_c</c> beside its
	/// <c>.shader_c</c>, loaded once; null when it wasn't compiled. Same modes, features and combos as the shader.
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
			if ( File.Exists( path ) ) variant = ShaderFile.Load( path );
		}

		lock ( gpuDriven ) gpuDriven[shader] = variant;
		return variant;
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
		if ( !gpuDriven ) return new ShaderVariant( this, shader, drawMode, vertex, pixel, compute );

		// A GPU-driven variant holds nothing of a material's, so materials with the same combos share one: their draws
		// can then share a pipeline and a multi-draw
		var key = $"{shader.Name}|{drawMode.Name}|{Describe( vertex )}|{Describe( pixel )}";
		lock ( gpuDrivenVariants )
		{
			if ( gpuDrivenVariants.TryGetValue( key, out var shared ) ) return shared;
			return gpuDrivenVariants[key] = new ShaderVariant( this, shader, drawMode, vertex, pixel, compute );
		}

		static string Describe( ShaderStage stage ) => stage is null ? "-" : $"{stage.Static.Id}:{stage.Dynamic.Id}:{string.Join( ',', stage.Constants.Select( c => $"{c.Id}={c.Value}" ) )}";
	}

	readonly Dictionary<string, ShaderVariant> gpuDrivenVariants = new();

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
