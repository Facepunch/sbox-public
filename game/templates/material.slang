// A material shader written as a surface: Evaluate says what the surface is at each pixel, and the engine lights it and draws
// it in every pass (forward, depth, shadows). Name it in a material as shaders/<this file's name>.shader; the struct's fields
// are the material's values and textures, which the material editor shows. See docs/reference/borealis-slang-materials.md.
import sbox.surface;

[Material, Features( "Common" ), Vertex( "Common" )]
public struct NewMaterial : ISurface
{
	[Group( "Color" ), Srgb, Opacity] Texture2D Color;
	[Group( "Color" ), Color, Default3( 1, 1, 1 )] float3 Tint;
	[Group( "Roughness" ), Range( 0, 1 ), Default( 1 )] float Roughness;
	[Group( "Metalness" ), Range( 0, 1 )] float Metalness;

	public Material Evaluate( SurfaceInput i )
	{
		float4 color = i.Sample( Color, i.Uv );

		Material m = Material.Init( i );
		m.Albedo = color.rgb * Tint * i.Color.rgb;
		m.Opacity = color.a;
		m.Roughness = Roughness;
		m.Metalness = Metalness; // 1 makes it a metal
		return m;
	}
}
