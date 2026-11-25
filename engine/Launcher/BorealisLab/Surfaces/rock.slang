// A material written as a surface: colour, normal map, roughness-metalness-occlusion, the job of simple.shader.
// Compiled by Sandbox.ShaderCompiler into the engine's forward, depth and alpha-tested depth passes; the same material as
// a .shader is engine/Tests/Borealis.Gpu.Test/Content/shaders/surface_test_rock.shader (the tests'), and as a raw module
// rock_raw.slang.
import sbox.surface;

[Material]
public struct Rock : ISurface
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
}
