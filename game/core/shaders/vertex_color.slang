// vertex_color.shader written as a surface: the colour texture by the mesh's vertex colour (sRGB, by the object's tint),
// metalness as a value and ambient occlusion, lit through the final combiner as vertex_color.shader is
// (vr_shared_standard_ps_code.fxc's standard processing without normal maps: the geometric normal, roughness 0). The
// vertex colour is the surface's Varyings, read from the mesh and interpolated to Evaluate.
import sbox.surface;

// g_sTrilinearWrap, for occlusion
SamplerState g_VertexColorTrilinear < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); >;

public struct VertexColorPaint
{
	public float4 Color : COLOR0;
}

[Material]
public struct VertexColor : IInterpolatedCombinerSurface
{
	typealias Varyings = VertexColorPaint;

	[Group( "Color" ), Srgb, Opacity] Texture2D Color;
	[Group( "Color" ), Default( 1 ), Range( 1, 16 )] float FadeExponent;
	[Group( "Texture Coordinates" ), Default2( 1, 1 )] float2 TexCoordScale;
	[Group( "Texture Coordinates" )] float2 TexCoordOffset;
	[Group( "Texture Coordinates" )] float2 TexCoordScrollSpeed;
	[Group( "Metalness" ), Range( 0, 1 )] float Metalness;
	[Group( "Ambient Occlusion" )] Texture2D AmbientOcclusion;

	/// The vertex stage's colour: linear, as the shader converts it per vertex.
	public VertexColorPaint Vertex( VertexColorPaint v )
	{
		v.Color.rgb = SrgbGammaToLinear( v.Color.rgb );
		return v;
	}

	public Material Evaluate( SurfaceInput i, VertexColorPaint v )
	{
		float2 uv = i.Uv * TexCoordScale + TexCoordOffset + TexCoordScrollSpeed * i.Time;
		Material m = Material.Init( i );
		m.Albedo = i.Sample( Color, uv ).rgb * v.Color.rgb * i.Color.rgb;
		m.Opacity = pow( i.Color.a, FadeExponent );
		m.Roughness = 0;
		m.Metalness = Metalness;
		m.AmbientOcclusion = i.Sample( AmbientOcclusion, uv, g_VertexColorTrilinear ).r;
		return m;
	}
}
