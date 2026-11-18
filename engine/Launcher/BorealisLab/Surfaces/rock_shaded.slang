// rock.slang shading itself (IShadedSurface): its material lit through the engine's lighting and fogged as the standard
// shading model does. Drawn against rock.slang, it must draw the same.
import sbox.surface;

[Material]
public struct RockShaded : IShadedSurface
{
	[Group( "Color" ), Srgb, Opacity] Texture2D Color;
	[Group( "Normal" )] Texture2D Normal;
	[Group( "Roughness" )] Texture2D Rma;
	[Group( "Color" ), Default3( 1, 1, 1 )] float3 Tint;
	[Group( "Roughness" ), Range( 0, 2 ), Default( 1 )] float RoughnessScale;

	public Material Evaluate( SurfaceInput i )
	{
		float4 color = i.Sample( Color, i.Uv );
		float3 rma = i.Sample( Rma, i.Uv ).rgb;
		float2 n = i.Sample( Normal, i.Uv ).xy * 2 - 1;

		Material m = {};
		m.Albedo = color.rgb * Tint * i.Color.rgb;
		m.Opacity = color.a;
		m.Roughness = saturate( rma.r * RoughnessScale );
		m.Metalness = rma.g;
		m.AmbientOcclusion = rma.b;
		m.Normal = i.ToWorld( float3( n, sqrt( saturate( 1 - dot( n, n ) ) ) ) );
		return m;
	}

	public float4 Shade<L : ISurfaceLighting>( SurfaceInput i, L lighting, out Material m )
	{
		m = lighting.ApplyDecals( Evaluate( i ) );
		float3 color = lighting.LightMaterial( m ).Color( m );
		if ( lighting.FogEnabled() ) color = lighting.VolumetricFog( lighting.Fog( color ) );
		return float4( color, m.Opacity );
	}
}
