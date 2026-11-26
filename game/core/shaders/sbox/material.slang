// sbox.material: a new-tier material's parameters. Both tiers declare them as a [Material] struct; the shader compiler
// turns its Texture2D fields into MaterialTexture2D (a bindless slot) and lays the struct out as one record the engine
// writes once per material. See docs/reference/borealis-slang-materials.md.
module material;

// Every sampled texture the device has, in the bindless set: t16 in space 4 is set 4, binding 62 with vfx's binding
// shifts, where the engine's Bindless.hlsl reads them (vk::binding isn't used: the shifts override it)
Texture2D g_MaterialTextures2D[] : register( t16, space4 );

// The same table's cube textures, and the bindless samplers (s15 in space 4), as Bindless.hlsl declares them: for slots
// the engine hands a surface in its own buffers (terrain's layers, a custom environment cubemap)
TextureCube g_MaterialTexturesCube[] : register( t16, space4 );
SamplerState g_MaterialSamplers[2048] : register( s15, space4 );

// The material records, and where this draw's is. A record's offset reaches the pixel stage one of two ways, picked by
// the pipeline (specialization constant 2): per pixel, a nointerpolation value the vertex stage passes on (the default:
// one GPU-driven bucket per pipeline draws many materials), or per draw, read straight from the draw's constant (uniform,
// so the record is read with scalar loads; buckets split by material).
ByteAddressBuffer g_SurfaceMaterials;
struct SurfaceDraw_t { uint MaterialOffset; };
[[vk::push_constant]] ConstantBuffer<SurfaceDraw_t> g_SurfaceDraw;

[vk::constant_id( 2 )] public const bool kMaterialOffsetPerPixel = true;

// The filtering SurfaceInput.Sample reads material textures with: common_samplers.fxc's g_sAniso
public SamplerState g_MaterialFiltering < Filter( ANISOTROPIC ); MaxAniso( 8 ); >;

/// The draw's material record offset, for the vertex stage to read records with and pass on.
public uint DrawMaterialOffset()
{
	return g_SurfaceDraw.MaterialOffset;
}

/// The record offset the pixel stage reads: what the vertex stage passed on, or the draw's own.
public uint PixelMaterialOffset( uint fromVertex )
{
	return kMaterialOffsetPerPixel ? fromVertex : g_SurfaceDraw.MaterialOffset;
}

/// A material record at <offset>, as a [Material] struct.
public T LoadMaterial<T>( uint offset )
{
	return g_SurfaceMaterials.Load<T>( offset );
}

/// This draw's material record, as a [Material] struct (the per-draw form).
public T LoadMaterial<T>()
{
	return g_SurfaceMaterials.Load<T>( g_SurfaceDraw.MaterialOffset );
}

/// A texture of the bindless table by its slot (one the engine wrote to a buffer or attribute, as Bindless::GetTexture2D
/// reads it); slot + 1 is a colour texture's sRGB view.
public MaterialTexture2D BindlessTexture2D( uint slot )
{
	MaterialTexture2D texture;
	texture.Slot = slot;
	return texture;
}

/// A cube texture of the bindless table by its slot (Bindless::GetTextureCube).
public TextureCube BindlessTextureCube( uint slot )
{
	return g_MaterialTexturesCube[NonUniformResourceIndex( slot )];
}

/// A bindless sampler by its slot (Bindless::GetSampler): the slot must be uniform across the wave.
public SamplerState BindlessSampler( uint slot )
{
	return g_MaterialSamplers[slot];
}

/// A material texture: its slot in the bindless table. Written Texture2D in a [Material] struct.
public struct MaterialTexture2D
{
	public uint Slot;

	public Texture2D Get() { return g_MaterialTextures2D[NonUniformResourceIndex( Slot )]; }
	public float4 Sample( SamplerState s, float2 uv ) { return Get().Sample( s, uv ); }
	public float4 SampleBias( SamplerState s, float2 uv, float bias ) { return Get().SampleBias( s, uv, bias ); }
	public float4 SampleLevel( SamplerState s, float2 uv, float lod ) { return Get().SampleLevel( s, uv, lod ); }
	public float4 SampleGrad( SamplerState s, float2 uv, float2 ddx, float2 ddy ) { return Get().SampleGrad( s, uv, ddx, ddy ); }
	public float4 Load( int3 location ) { return Get().Load( location ); }
	public uint2 Size()
	{
		uint width, height;
		Get().GetDimensions( width, height );
		return uint2( width, height );
	}
}

// Attributes the compiler reads from [Material] structs (and the generated C# class shows)
[__AttributeUsage( _AttributeTargets.Struct )]
public struct MaterialAttribute {};

[__AttributeUsage( _AttributeTargets.Var )]
public struct GroupAttribute { string Name; };

[__AttributeUsage( _AttributeTargets.Var )]
public struct RangeAttribute { float Min; float Max; };

[__AttributeUsage( _AttributeTargets.Var )]
public struct DefaultAttribute { float X; };

[__AttributeUsage( _AttributeTargets.Var )]
public struct Default2Attribute { float X; float Y; };

[__AttributeUsage( _AttributeTargets.Var )]
public struct Default3Attribute { float X; float Y; float Z; };

[__AttributeUsage( _AttributeTargets.Var )]
public struct Default4Attribute { float X; float Y; float Z; float W; };

// The texture Opacity comes from: alpha to coverage compensates its mips by its size, as today's shaders do
[__AttributeUsage( _AttributeTargets.Var )]
public struct OpacityAttribute {};

// A feature pipelines are specialised for (a bool or int, fixed per material): each material's value folds its branches
// in pixel stages, at the cost of a pipeline and a GPU-driven bucket per value. For features whose code is large where
// most materials don't use it (unlit), not for ones every pipeline pays for anyway
[__AttributeUsage( _AttributeTargets.Var )]
public struct SpecializeAttribute {};

// A texture read in sRGB (colour); without it a texture is read linear (normals, masks)
[__AttributeUsage( _AttributeTargets.Var )]
public struct SrgbAttribute {};

// Where a material converted from a .vmat takes this field from: a .shader feature (F_MULTIBLEND), variable or
// ShaderVersion. Without it the field is found by name (g_tName, g_flName, g_vName, g_nName, g_bName, Name)
[__AttributeUsage( _AttributeTargets.Var )]
public struct SourceAttribute { string Name; };

// How the surface draws (its render state), on the [Material] struct; the engine resolves it per material and picks
// the layer from it. Blend: how the colour meets what's behind it: "Opaque" (the default), "Alpha" (over it by
// Opacity), "Premultiplied" (colour already multiplied by Opacity), "Additive" (added, by Opacity), "Multiply" (the
// colour behind times the colour), "Mod2x" (the colour behind times twice the colour), "ModulateAdd" (the colour behind
// times the colour, plus the colour behind). A blended surface draws in the translucent layer and writes no depth
[__AttributeUsage( _AttributeTargets.Struct )]
public struct BlendAttribute { string Mode; };

// Write depth even when blended
[__AttributeUsage( _AttributeTargets.Struct )]
public struct DepthWriteAttribute {};

// Don't write depth, even when opaque
[__AttributeUsage( _AttributeTargets.Struct )]
public struct NoDepthWriteAttribute {};

// Don't test depth: draw over everything
[__AttributeUsage( _AttributeTargets.Struct )]
public struct NoDepthTestAttribute {};

// Pull the surface towards the camera in depth: a constant in depth-buffer steps and a factor of the slope (decals)
[__AttributeUsage( _AttributeTargets.Struct )]
public struct DepthBiasAttribute { float Constant; float Slope; };

// The final combiner's compile-time lighting features a combiner surface lights with, as a .shader's COMMON block defines
// them: "S_TWO_LOBE_SPECULAR, S_TRANSMISSIVE, S_SUBSURFACE_SCATTERING = SUBSURFACE_SCATTERING_PREINTEGRATED" (a name alone
// is 1). Material's fields for them (SssNormal, TwoLobeSpecularRatio, ...) are read only where they're on
[__AttributeUsage( _AttributeTargets.Struct )]
public struct CombinerAttribute { string Defines; };

// Defines ahead of the engine's includes for any surface, as a .shader's COMMON block sets them for the lighting:
// "S_DIFFUSE_TWO_SIDED" (water's), "A = B" (a name alone is 1). [Combiner( ... )] is the same for a combiner surface
[__AttributeUsage( _AttributeTargets.Struct )]
public struct LightingAttribute { string Defines; };

// How many of SurfaceVertex.ClipDistance an IVertexStageSurface writes (1 to 4): each is a clip distance in every pass
[__AttributeUsage( _AttributeTargets.Struct )]
public struct ClipDistancesAttribute { int Count; };

// Draw back faces too: a combiner surface's normal turned to the viewer, as complex.shader does; others see the face's
// own, as common/pixel.hlsl's shaders do (SurfaceInput.FrontFace says which side)
[__AttributeUsage( _AttributeTargets.Struct )]
public struct TwoSidedAttribute {};

// Clip pixels whose opacity is under the cutoff, with alpha to coverage under multisampling
[__AttributeUsage( _AttributeTargets.Struct )]
public struct AlphaTestAttribute {};

// Draw in the translucent layer (after the opaque one, sorted, with opacity kept in alpha) even when not blended: for a
// surface that replaces what's behind it with what it reads of the frame-buffer copy (refracting glass). It has no depth
// passes of its own, as a blended surface hasn't
[__AttributeUsage( _AttributeTargets.Struct )]
public struct TranslucentAttribute {};

// A field a material changes its render state with: "State" (the field's value is the state: a Blend by its index in
// the list above, a flag by non-zero), "State: Value" (non-zero sets it), or "State: A, B, C" (the field's value picks
// one); several separated by ';'. States: Blend, DepthWrite, DepthTest, TwoSided, AlphaTest, Translucent; flag values
// true or false.
// For example [Source( "F_TRANSLUCENT" ), RenderState( "Blend: Alpha" )] bool UseTranslucent;
[__AttributeUsage( _AttributeTargets.Var )]
public struct RenderStateAttribute { string Rule; };

// What the material compiler, the material editor and the model compiler read of a surface, generated from these
// (SurfaceDeclarations; docs/reference/borealis-slang-materials.md, "Declaring a surface's material"). Conditions and
// expressions name the struct's fields ("Quality != 1"), read as the features they take.

// A feature a .vmat sets this bool or int field with, which the material editor offers: its range is the field's [Range]
// (0..1 for a bool), its pane the field's [Group]. One of the struct's [Features] set's is taken, not declared again
[__AttributeUsage( _AttributeTargets.Var )]
public struct FeatureAttribute { string Name; };

// A feature's values by name, separated by '|': "None | Mod2X | Overlay"
[__AttributeUsage( _AttributeTargets.Var )]
public struct OptionsAttribute { string Names; };

// A feature rule the material editor and the material compiler apply ("Allow1( UseAlphaTest, UseTranslucent )"), with
// the message the editor shows, following the feature field's own declaration
[__AttributeUsage( _AttributeTargets.Var )]
public struct RuleAttribute { string Rule; string Message; };

// The material editor shows the field (a parameter, or a texture's inputs) only where the condition holds
[__AttributeUsage( _AttributeTargets.Var )]
public struct ShowIfAttribute { string Condition; };

// The texture, and its inputs, exist only where the condition holds (the material compiler makes it only then)
[__AttributeUsage( _AttributeTargets.Var )]
public struct WhenAttribute { string Condition; };

// A float3 the material editor shows as a colour
[__AttributeUsage( _AttributeTargets.Var )]
public struct ColorAttribute {};

// How the material compiler makes the texture from the artist's inputs: channels from inputs ("RGB: Color; A: TintMask",
// an algorithm as "RGBA: HemiOctIsoRoughness_RG_B( Normal, Roughness )", Box by default) and the output format. A short
// input name is the input Texture<Name>; its colour space, file ending, processor and default follow its name (Color,
// Normal, Roughness, Metalness, AmbientOcclusion, ...) unless an [Input] gives them
[__AttributeUsage( _AttributeTargets.Var )]
public struct PackAttribute { string Channels; string Format; };

// A pack used where a condition holds; the first that holds is the texture's
[__AttributeUsage( _AttributeTargets.Var )]
public struct PackIfAttribute { string Channels; string Format; string Condition; };

// An input a pack reads, with its pane ("Color/10"); the rest by its name's convention
[__AttributeUsage( _AttributeTargets.Var )]
public struct InputAttribute { string Name; string Group; };

// An input with what its convention doesn't give: "Srgb", "Linear", a processor ("Inverse", "None"), a file ending
// ("_mask"), a default ("Default3( 0.86, 0.67, 0.62 )", "Default3AndFile( 0, 0, 0, \"x.tga\" )"), separated by ';'
[__AttributeUsage( _AttributeTargets.Var )]
public struct InputWithAttribute { string Name; string Group; string Settings; };

// The engine's shared feature set the surface offers with its own: "Common" (common/features.hlsl) or "Sbox"
// (vr_common_features.fxc)
[__AttributeUsage( _AttributeTargets.Struct )]
public struct FeaturesAttribute { string Set; };

// The engine's shared material inputs the surface's textures are: "Standard" (the standard processing's colour, normal,
// tint mask, occlusion, metalness, self-illumination, detail and transmission, by the struct's features)
[__AttributeUsage( _AttributeTargets.Struct )]
public struct InputsAttribute { string Set; };

// A material attribute the engine reads (BoolAttribute), true
[__AttributeUsage( _AttributeTargets.Struct )]
public struct AttributeAttribute { string Name; };

// A texture attribute naming this texture (RepresentativeTexture: what tools map a face's uvs by)
[__AttributeUsage( _AttributeTargets.Var )]
public struct TextureAttributeAttribute { string Name; };

// A material attribute set to a condition over the fields, worked out for each material ("Quality != 1")
[__AttributeUsage( _AttributeTargets.Struct )]
public struct AttributeOfAttribute { string Name; string Condition; };

// A material attribute kept as an expression of the material's features, as vfx keeps one ("DoNotCastShadows")
[__AttributeUsage( _AttributeTargets.Struct )]
public struct AttributeExpressionAttribute { string Name; string Condition; };

// A material attribute set (true) only where a condition holds
[__AttributeUsage( _AttributeTargets.Struct )]
public struct AttributeIfAttribute { string Name; string Condition; };

// The vertex input signature the model compiler lays meshes out by: a base ("Common", "Standard" or "Sbox": the engine's
// vertex inputs) and extra streams ("Color", "VertexPaintBlend", "VertexPaintTint", "SecondaryUv"), each with an
// optional condition ("Color if Animation == 1"), separated by ','
[__AttributeUsage( _AttributeTargets.Struct )]
public struct VertexAttribute { string Inputs; };

// A packed texture type's packing ("Rma"), which the compiler writes for a field declared with that type (Rma Rma;)
[__AttributeUsage( _AttributeTargets.Var )]
public struct PackedAttribute { string Type; };

// Fields the engine binds rather than the material, which the compiler makes module globals bound by attribute outside
// the record (a module the surface imports writes its own the same way, at its top level): [Engine( "Attribute" )] with
// [Srgb], [Default( x )] and [Specialized] (a specialization constant) as wanted, and [Sampler( "Trilinear", "Mirror" )]
// on a SamplerState ("Anisotropic 8" for a maximum). Each is on one line. They never reach Slang as attributes.
