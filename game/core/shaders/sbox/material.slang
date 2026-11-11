// sbox.material: a new-tier material's parameters. Both tiers declare them as a [Material] struct; the shader compiler
// turns its Texture2D fields into MaterialTexture2D (a bindless slot) and lays the struct out as one record the engine
// writes once per material. See docs/managed/shader-format-replacement.md.
module material;

// Every sampled texture the device has, in the bindless set: t16 in space 4 is set 4, binding 62 with vfx's binding
// shifts, where the engine's Bindless.hlsl reads them (vk::binding isn't used: the shifts override it)
Texture2D g_MaterialTextures2D[] : register( t16, space4 );

// The material records and the record this draw reads. The offset is uniform per draw: a record read at a per-pixel
// offset costs the big pixel shaders registers (designs/shaderauthoring.md).
ByteAddressBuffer g_SurfaceMaterials;
struct SurfaceDraw_t { uint MaterialOffset; };
[[vk::push_constant]] ConstantBuffer<SurfaceDraw_t> g_SurfaceDraw;

/// This draw's material record, as a [Material] struct.
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

// A texture read in sRGB (colour); without it a texture is read linear (normals, masks)
[__AttributeUsage( _AttributeTargets.Var )]
public struct SrgbAttribute {};
