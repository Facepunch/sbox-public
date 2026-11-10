namespace Basalt.Source2.Shaders;

/// <summary>
/// The programs a shader can have (<c>VfxProgram_t</c>), in file order.
/// </summary>
public enum ShaderProgramType
{
	/// <summary>Features, modes and the material editor's variables; no code.</summary>
	Features,
	/// <summary>Vertex shader.</summary>
	Vertex,
	/// <summary>Pixel shader.</summary>
	Pixel,
	/// <summary>Geometry shader.</summary>
	Geometry,
	/// <summary>Hull shader (unused).</summary>
	Hull,
	/// <summary>Domain shader (unused).</summary>
	Domain,
	/// <summary>Compute shader.</summary>
	Compute,
	/// <summary>Pixel shader render state only.</summary>
	PixelRenderState,
	/// <summary>Ray tracing.</summary>
	RayTracing,
}

/// <summary>
/// Where a variable's value comes from (<c>VfxVariableSourceType_t</c>).
/// </summary>
public enum VariableSource
{
	/// <summary>The material (artist-set).</summary>
	Material = 0,
	/// <summary>A render attribute the engine sets, named by <see cref="ShaderVariable.Attribute"/>.</summary>
	Attribute,
	/// <summary>A feature's value as an int.</summary>
	FeatureToInt,
	/// <summary>A feature's value as a bool.</summary>
	FeatureToBool,
	/// <summary>A feature's value as a float.</summary>
	FeatureToFloat,
	/// <summary>A render state or sampler state set to <see cref="ShaderVariable.IntDefault"/>.</summary>
	RenderStateLiteral,
	/// <summary>A compiled expression.</summary>
	Expression,
	/// <summary>The material, with an expression.</summary>
	MaterialAndExpression,
	/// <summary>The viewport.</summary>
	Viewport,
	/// <summary>One over the viewport size.</summary>
	InvViewportSize,
	/// <summary>A texture's size.</summary>
	TextureDimensions,
	/// <summary>One over a texture's size.</summary>
	InvTextureDimensions,
	/// <summary>Log2 of a texture's size.</summary>
	TextureDimensionsLog2,
	/// <summary>A texture's sheet data.</summary>
	TextureSheetData,
	/// <summary>Tools shading complexity.</summary>
	ShadingComplexity,
	/// <summary>Tools shader id colour.</summary>
	ShaderIdColor,
	/// <summary>A descriptor set bound by code (the bindless set).</summary>
	ExternalDescriptorSet,
	/// <summary>Mapping dimensions.</summary>
	MappingDimensions,
	/// <summary>The shader's version.</summary>
	ShaderVersion,
}

/// <summary>
/// A variable's type (<c>VfxVariableType_t</c>).
/// </summary>
public enum VariableType
{
#pragma warning disable CS1591
	Undefined = 0,
	Float, Float2, Float3, Float4,
	Int, Int2, Int3, Int4,
	Bool, Bool2, Bool3, Bool4,
	Texture1D, Texture2D, Texture3D, TextureCube,
	Matrix3x3, Matrix3x4, Matrix4x4,
	Struct, ConstantBuffer,
	TextureCubeArray, Texture2DArray, Buffer, Texture1DArray, Texture3DArray,
	StructuredBuffer, ByteAddressBuffer,
	RWBuffer, RWTexture1D, RWTexture1DArray, RWTexture2D, RWTexture2DArray, RWTexture3D,
	RWStructuredBuffer, RWByteAddressBuffer, AppendStructuredBuffer, ConsumeStructuredBuffer, RWStructuredBufferWithCounter,
	DescriptorSet, Interface, Class, SamplerState,
#pragma warning restore CS1591
}

/// <summary>
/// What kind of register a variable occupies (<c>VfxRegisterType_t</c>).
/// </summary>
public enum RegisterType
{
#pragma warning disable CS1591
	Unknown = 0,
	Bool,
	Int4,
	Float4,
	Texture,
	RenderState,
	SamplerState,
	InputTexture,
	ConstantBuffer,
	Uav,
	DescriptorSet,
	PushConstantBuffer,
	Specialization,
#pragma warning restore CS1591
}

/// <summary>
/// A static or dynamic combo (<c>CVfxCombo</c>): a name and a range. Its <see cref="Stride"/> is what its value is
/// multiplied by in a combo id.
/// </summary>
public sealed class ShaderCombo
{
	/// <summary>Its name, <c>S_*</c>, <c>D_*</c> or a feature's <c>F_*</c>.</summary>
	public string Name { get; init; }

	/// <summary>The material editor group.</summary>
	public string Group { get; init; }

	/// <summary>Whether it's a specialization constant rather than separate code (<c>ShaderConstant()</c>).</summary>
	public bool Specialization { get; init; }

	/// <summary>Its range.</summary>
	public int Min { get; init; }

	/// <summary>Its range.</summary>
	public int Max { get; init; }

	/// <summary>Where its value comes from: 0 a feature or the mode (static), an attribute (dynamic); see the docs.</summary>
	public int Source { get; init; }

	/// <summary>The feature it follows, an index into the features program's combos, or -1.</summary>
	public int FeatureIndex { get; init; }

	/// <summary>Feature value names, for features.</summary>
	public string[] Strings { get; init; }

	/// <summary>How many values it has.</summary>
	public int Count => Max - Min + 1;

	/// <summary>What one step of its value adds to a combo id.</summary>
	public ulong Stride { get; internal set; }

	/// <inheritdoc/>
	public override string ToString() => $"{Name} {Min}..{Max}";
}

/// <summary>
/// A variable a program reads (<c>CVfxVariableDescription</c>): a material parameter, an engine attribute, a texture,
/// a constant buffer, a sampler or render state value.
/// </summary>
public sealed class ShaderVariable
{
	/// <summary>Its name in the shader.</summary>
	public string Name { get; init; }

	/// <summary>The attribute it's bound to, for <see cref="VariableSource.Attribute"/>.</summary>
	public string Attribute { get; init; }

	/// <summary>Where its value comes from.</summary>
	public VariableSource Source { get; init; }

	/// <summary>
	/// The source's argument: for texture-derived sources (<c>Source( InvTextureDim ); SourceArg( g_tColor )</c>), the index
	/// of the texture's variable in the program's table; -1 for none.
	/// </summary>
	public int SourceIndex { get; init; } = -1;

	/// <summary>Its type.</summary>
	public VariableType Type { get; init; }

	/// <summary>The kind of register it's in.</summary>
	public RegisterType Register { get; init; }

	/// <summary>Registers (components) it covers.</summary>
	public int RegisterElements { get; init; }

	/// <summary>For render and sampler states, which field it sets; for external constant buffers, which one.</summary>
	public int RenderStateId { get; init; }

	/// <summary>The material editor's default texture.</summary>
	public string DefaultTexture { get; init; }

	/// <summary>Default for ints and bools; the value of render-state literals.</summary>
	public int[] IntDefault { get; init; }

	/// <summary>Default for floats.</summary>
	public float[] FloatDefault { get; init; }

	/// <summary>Its compiled expression, for expression sources.</summary>
	public byte[] Expression { get; init; }

	/// <summary>Whether it's a texture of some dimension.</summary>
	public bool IsTexture => Type is VariableType.Texture1D or VariableType.Texture2D or VariableType.Texture3D or VariableType.TextureCube
		or VariableType.TextureCubeArray or VariableType.Texture2DArray or VariableType.Texture1DArray or VariableType.Texture3DArray;

	/// <inheritdoc/>
	public override string ToString() => string.IsNullOrEmpty( Attribute ) ? Name : $"{Name} ({Attribute})";
}

/// <summary>
/// A constant buffer the engine fills, as the shader sees it (<c>ConstantBufferDescription_t</c>).
/// </summary>
public sealed class ConstantBufferLayout
{
	/// <summary>Its name, which is also the attribute it's set through.</summary>
	public string Name { get; init; }

	/// <summary>Its size in bytes.</summary>
	public int Size { get; init; }

	/// <summary>Its members; offsets in bytes.</summary>
	public (string Name, int Offset, int Columns, int Rows, int ArraySize)[] Members { get; init; }

	/// <inheritdoc/>
	public override string ToString() => $"{Name} ({Size} bytes)";
}

/// <summary>
/// One vertex input in a VS input signature.
/// </summary>
public readonly record struct InputElement( string Name, string D3DSemantic, string Semantic, int SemanticIndex );

/// <summary>
/// A mode (<c>CVfxMode</c>): a pass a shader draws in, the static combos it sets, or another shader it falls back to.
/// </summary>
public sealed class ShaderMode
{
	/// <summary>Its name: <c>Forward</c>, <c>Depth</c>...</summary>
	public string Name { get; init; }

	/// <summary>Another shader that draws this mode instead, or empty.</summary>
	public string Fallback { get; init; }

	/// <summary>Static combos the mode sets.</summary>
	public (string Combo, int Value)[] Settings { get; init; }

	/// <inheritdoc/>
	public override string ToString() => Name;
}

/// <summary>
/// A variable's place in a combo: the variable, its descriptor set and its register offset (a texture or sampler
/// register, a constant's offset in 4-byte units, a specialization constant id, or 0xFFFF for render state).
/// </summary>
public readonly record struct VariableBinding( int Variable, int Set, int Offset );

/// <summary>
/// A shader attribute a static combo exports (<c>CVfxShaderAttribute</c>), with its literal value when it has one.
/// </summary>
public readonly record struct ShaderAttribute( string Name, VariableType Type, int Binding, object Value );
