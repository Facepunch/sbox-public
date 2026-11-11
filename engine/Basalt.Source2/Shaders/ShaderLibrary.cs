using Basalt.Gpu;
using System.IO;
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
	/// The variant of <paramref name="shader"/> that draws <paramref name="mode"/> for a material with these feature
	/// values (by feature index; see <see cref="ShaderFile.FeatureIndex"/>), with dynamic combos from
	/// <paramref name="dynamicCombos"/> and specialization attributes from <paramref name="attributes"/>. Null when the
	/// shader has no such mode. Throws if the combo was never compiled.
	/// </summary>
	public ShaderVariant Variant( ShaderFile shader, string mode, ReadOnlySpan<int> features, Func<string, int?> dynamicCombos = null, Func<string, int?> attributes = null )
	{
		var drawMode = shader.Mode( mode );
		if ( drawMode is null ) return null;
		if ( !string.IsNullOrEmpty( drawMode.Fallback ) )
		{
			var fallback = Load( drawMode.Fallback.Contains( '/' ) ? drawMode.Fallback : $"shaders/{drawMode.Fallback}" );
			return fallback is null ? null : Variant( fallback, mode, [], dynamicCombos, attributes );
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
		// pipelines, and draws of them one after another don't switch pipeline
		if ( !ShareVariants ) return new ShaderVariant( this, shader, drawMode, vertex, pixel, compute );
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
