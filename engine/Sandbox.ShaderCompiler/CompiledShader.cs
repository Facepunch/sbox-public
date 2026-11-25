namespace Sandbox.ShaderCompiler;

/// <summary>
/// A new-tier shader, compiled: SPIR-V for each of its passes with what each binding is, and the layout of its material
/// record. What a renderer needs to draw it, and what the typed C# class is generated from.
/// </summary>
public sealed class CompiledShader
{
	/// <summary>The material type's name (<c>Rock</c>), which is also the generated class's.</summary>
	public string Name { get; init; }

	/// <summary>Which tier it was written in.</summary>
	public ShaderTier Tier { get; init; }

	/// <summary>The source file.</summary>
	public string Path { get; init; }

	/// <summary>Its material parameters: one record, as the passes read it.</summary>
	public MaterialLayout Material { get; init; }

	/// <summary>Its passes by name: <see cref="ShaderPasses.Forward"/>, <see cref="ShaderPasses.Depth"/>, <see cref="ShaderPasses.DepthAlphaTest"/>.</summary>
	public IReadOnlyDictionary<string, CompiledPass> Passes { get; init; }

	/// <summary>The instance file the compiler wrote for a surface (null for a raw module), for diagnostics.</summary>
	public string Generated { get; init; }

	/// <summary>Why a surface has no GPU-driven passes (its GPU-driven compile's errors); null when it has them.</summary>
	public string GpuDrivenError { get; init; }

	/// <summary>Compile time, all passes; the cache lookup's when <see cref="FromCache"/>.</summary>
	public double Milliseconds { get; init; }

	/// <summary>Every file the compile read (the module, the modules it imports, the core includes), full paths.</summary>
	public IReadOnlyList<string> Dependencies { get; init; } = [];

	/// <summary>Whether it came from the compiler's disk cache (<see cref="SlangCompiler.CacheFolder"/>).</summary>
	public bool FromCache { get; init; }

	/// <summary>Whether it came from its source's offline compile (<see cref="SlangOutput"/>), which build-shaders makes.</summary>
	public bool Offline { get; init; }

	/// <summary>A pass, or null when the shader doesn't have it.</summary>
	public CompiledPass Pass( string name ) => Passes.TryGetValue( name, out var pass ) ? pass : null;
}

/// <summary>The two ways to write a new-tier shader.</summary>
public enum ShaderTier
{
	/// <summary>A <c>[Material]</c> struct implementing <c>ISurface</c>; the engine owns the passes.</summary>
	Surface,
	/// <summary>A module whose author owns the entry points.</summary>
	Raw,
}

/// <summary>Pass names a renderer asks for.</summary>
public static class ShaderPasses
{
	/// <summary>Lit colour.</summary>
	public const string Forward = "Forward";

	/// <summary>Depth only, no pixel shader: the depth prepass and shadows of opaque materials.</summary>
	public const string Depth = "Depth";

	/// <summary>Depth with the material's opacity clipped: alpha-tested materials' prepass and shadows.</summary>
	public const string DepthAlphaTest = "DepthAlphaTest";

	/// <summary>
	/// A surface's passes with this suffix (<c>ForwardLightmapped</c>) have the vertex stage of lightmapped draws, which
	/// passes on the mesh's lightmap uv.
	/// </summary>
	public const string Lightmapped = "Lightmapped";

	/// <summary>
	/// A surface's passes with this suffix (<c>ForwardGpuDriven</c>, <c>ForwardGpuDrivenLightmapped</c>, <c>ForwardGpuDrivenSkinned</c>)
	/// draw GPU-driven: vertices pulled, the material record and lighting from the GPU-driven tables (docs/reference/borealis-gpu-driven.md).
	/// </summary>
	public const string GpuDriven = "GpuDriven";

	/// <summary>
	/// A surface's passes with this suffix (<c>ForwardSkinned</c>) draw skinned vertices from the vertex cache: the vertex
	/// stage compiled with <c>D_CS_VERTEX_ANIMATION</c>, the same pixel stage.
	/// </summary>
	public const string Skinned = "Skinned";
}

/// <summary>One pass: a vertex shader and, unless it draws depth alone, a pixel shader.</summary>
public sealed class CompiledPass
{
	/// <summary>The pass's name.</summary>
	public string Name { get; init; }

	/// <summary>The vertex stage.</summary>
	public CompiledStage Vertex { get; init; }

	/// <summary>The pixel stage; null to draw depth only.</summary>
	public CompiledStage Pixel { get; init; }
}

/// <summary>Which shader stage.</summary>
public enum StageKind
{
	/// <summary>Vertex.</summary>
	Vertex,
	/// <summary>Pixel (fragment).</summary>
	Pixel,
	/// <summary>Compute: a pass module's <c>MainCs</c>.</summary>
	Compute,
	/// <summary>Task (amplification): a pass module's <c>MainTs</c>, which launches its mesh groups.</summary>
	Task,
	/// <summary>Mesh: a pass module's <c>MainMs</c>, which emits vertices and primitives in place of a vertex stage.</summary>
	Mesh,
}

/// <summary>One entry point compiled: its SPIR-V and what it binds.</summary>
public sealed class CompiledStage
{
	/// <summary>Which stage.</summary>
	public StageKind Stage { get; init; }

	/// <summary>The entry point's name in the SPIR-V.</summary>
	public string Entry { get; init; }

	/// <summary>The SPIR-V. Resources are in set 0 for the vertex stage and set 1 for the pixel stage, as vfx puts them; set 4 is the bindless set.</summary>
	public byte[] Spirv { get; init; }

	/// <summary>Every descriptor binding the SPIR-V uses, with what it's for.</summary>
	public IReadOnlyList<ShaderBinding> Bindings { get; init; }

	/// <summary>The stage's <c>$Globals</c> constant buffer, or null when it has none.</summary>
	public GlobalConstants Globals { get; init; }

	/// <summary>Vertex inputs (vertex stage), by location.</summary>
	public IReadOnlyList<VertexInputSlot> Inputs { get; init; }

	/// <summary>Specialization constants from <c>SpecializedConstant()</c> globals, set per pipeline from their attributes.</summary>
	public IReadOnlyList<SpecializationSlot> Specializations { get; init; } = [];
}

/// <summary>A specialization constant: its id, the global it was, and the attribute and default it takes.</summary>
public sealed record SpecializationSlot( int Id, string Name, Annotations Annotations )
{
	/// <summary>The attribute its value comes from.</summary>
	public string Attribute => Annotations?.Attribute ?? Name;
}

/// <summary>What a descriptor holds.</summary>
public enum BindingKind
{
	/// <summary>A constant buffer (<c>cbuffer</c>, <c>ConstantBuffer&lt;T&gt;</c>), bound by its name.</summary>
	ConstantBuffer,
	/// <summary>The stage's <c>$Globals</c> constant buffer (<see cref="CompiledStage.Globals"/>).</summary>
	Globals,
	/// <summary>A structured or byte-address buffer.</summary>
	Buffer,
	/// <summary>A sampled texture.</summary>
	Texture,
	/// <summary>A storage texture.</summary>
	StorageTexture,
	/// <summary>A sampler.</summary>
	Sampler,
	/// <summary>The material records (<see cref="MaterialLayout"/>).</summary>
	MaterialRecords,
}

/// <summary>A texture binding's shape.</summary>
public enum TextureShape
{
	/// <summary>Not a texture.</summary>
	None,
	/// <summary>2D.</summary>
	Texture2D,
	/// <summary>2D array.</summary>
	Texture2DArray,
	/// <summary>3D.</summary>
	Texture3D,
	/// <summary>Cube.</summary>
	Cube,
	/// <summary>Cube array.</summary>
	CubeArray,
}

/// <summary>
/// A descriptor binding: where, what, and the name the engine binds it by (its vfx <c>Attribute</c>, else the variable's
/// or constant buffer's own name).
/// </summary>
public sealed record ShaderBinding( string Variable, int Set, int Binding, BindingKind Kind, int Count, TextureShape Shape, Annotations Annotations )
{
	/// <summary>The name the engine binds it by.</summary>
	public string Name => Annotations?.Attribute ?? Variable;

	/// <summary>A constant buffer's size in bytes, as compiled; 0 for other bindings.</summary>
	public int Size { get; init; }
}

/// <summary>The <c>$Globals</c> constant buffer: where it is, how big, and its members.</summary>
public sealed record GlobalConstants( int Set, int Binding, int Size, IReadOnlyList<GlobalMember> Members );

/// <summary>A <c>$Globals</c> member: its byte offset, scalar kind and component count, and what fills it.</summary>
public sealed record GlobalMember( string Name, int Offset, ScalarKind Kind, int Components, Annotations Annotations )
{
	/// <summary>The attribute it's filled from: its annotation's, else its own name.</summary>
	public string Attribute => Annotations?.Attribute ?? Name;
}

/// <summary>A numeric component type.</summary>
public enum ScalarKind
{
	/// <summary>32-bit float.</summary>
	Float,
	/// <summary>32-bit signed integer.</summary>
	Int,
	/// <summary>32-bit unsigned integer.</summary>
	UInt,
	/// <summary>Bool, 32 bits.</summary>
	Bool,
}

/// <summary>A vertex input: its location, the HLSL semantic it reads (<c>TEXCOORD</c>, 13) and its type.</summary>
public sealed record VertexInputSlot( int Location, string Semantic, int SemanticIndex, ScalarKind Kind, int Components )
{
	/// <summary>The entry point's parameter field it fills (<c>i.vPositionOs</c>), for code that builds the input itself.</summary>
	public string Field { get; init; }
}
