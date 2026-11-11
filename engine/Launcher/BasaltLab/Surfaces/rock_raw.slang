// The raw tier: rock.slang's material with its author owning the entry points. It uses the engine's vertex processing
// and lighting from today's includes (sbox/engine.slangh) and could do anything else instead; its parameters are still
// a [Material] struct, read with LoadMaterial, so its material is one record with bindless textures exactly as a
// surface's is. Vertex entry points are compiled with PROGRAM 1 and pixel ones with PROGRAM 2, as a .shader's VS and PS
// blocks are, so code for one stage sits in an #if.
#include "sbox/engine.slangh"
import sbox.material;

[Material]
public struct RockRaw
{
	[Group( "Color" ), Srgb] Texture2D Color;
	[Group( "Normal" )] Texture2D Normal;
	[Group( "Roughness" )] Texture2D Rma;
	[Group( "Color" ), Default3( 1, 1, 1 )] float3 Tint;
	[Group( "Roughness" ), Range( 0, 2 ), Default( 1 )] float RoughnessScale;
}

#if ( PROGRAM == VFX_PROGRAM_VS )

[shader( "vertex" )]
PixelInput MainVs( VertexInput i )
{
	PixelInput o = ProcessVertex( i );
	return FinalizeVertex( o );
}

#else

[shader( "pixel" )]
float4 MainPs( PixelInput i ) : SV_Target0
{
	RockRaw rock = LoadMaterial<RockRaw>();
	float2 uv = i.vTextureCoords.xy;
	float4 color = rock.Color.Sample( g_sAniso, uv );
	float3 rma = rock.Rma.Sample( g_sAniso, uv ).rgb;
	float2 n = rock.Normal.Sample( g_sAniso, uv ).xy * 2 - 1;
	float3 tangentNormal = float3( n, sqrt( saturate( 1 - dot( n, n ) ) ) );

	Material m = Material::Init( i );
	m.Albedo = color.rgb * rock.Tint * i.vVertexColor.rgb;
	m.Opacity = color.a;
	m.Roughness = saturate( rma.r * rock.RoughnessScale );
	m.Metalness = rma.g;
	m.AmbientOcclusion = rma.b;
	m.Normal = normalize( tangentNormal.x * i.vTangentUWs + tangentNormal.y * i.vTangentVWs + tangentNormal.z * normalize( i.vNormalWs.xyz ) );
	return ShadingModelStandard::Shade( i, m );
}

#endif
