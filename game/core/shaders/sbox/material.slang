// sbox.material: a new-tier material's parameters. Both tiers declare them as a [Material] struct; the shader compiler
// turns its Texture2D fields into MaterialTexture2D (a bindless slot) and lays the struct out as one record the engine
// writes once per material. See docs/managed/shader-format-replacement.md.
module material;

// Every sampled texture the device has, in the bindless set: t16 in space 4 is set 4, binding 62 with vfx's binding
// shifts, where the engine's Bindless.hlsl reads them (vk::binding isn't used: the shifts override it)
Texture2D g_MaterialTextures2D[] : register( t16, space4 );

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

// A texture read in sRGB (colour); without it a texture is read linear (normals, masks)
[__AttributeUsage( _AttributeTargets.Var )]
public struct SrgbAttribute {};
