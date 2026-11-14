// rock.slang with a light term of its own: wrapped diffuse past the terminator, from every light the engine's light loop
// shades the pixel with. At zero WrapStrength it draws exactly as Rock does.
import sbox.surface;

[Material]
public struct RockLit : ILitSurface
{
	[Group( "Color" ), Srgb, Opacity] Texture2D Color;
	[Group( "Normal" )] Texture2D Normal;
	[Group( "Roughness" )] Texture2D Rma;
	[Group( "Color" ), Default3( 1, 1, 1 )] float3 Tint;
	[Group( "Roughness" ), Range( 0, 2 ), Default( 1 )] float RoughnessScale;
	[Group( "Wrap" ), Range( 0, 1 ), Default( 0.5 )] float WrapAmount;
	[Group( "Wrap" ), Range( 0, 2 ), Default( 0 )] float WrapStrength;

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

	public float3 Light( Material m, SurfaceInput i, LightSample light )
	{
		float ndl = dot( m.Normal, light.Direction );
		float wrapped = saturate( ( ndl + WrapAmount ) / ( 1.0 + WrapAmount ) );
		return m.Albedo * light.Color * max( wrapped - saturate( ndl ), 0.0 ) * WrapStrength * light.Attenuation * light.Visibility;
	}
}
