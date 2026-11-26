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

[Material, Features( "Sbox" ), Vertex( "Standard, Color" ),
	Attribute( "NeedsLightProbe" ), Attribute( "CanBatchWithDynamicShaderConstants" ), AttributeExpression( "DoNotCastShadows", "F_DO_NOT_CAST_SHADOWS" ),
	Attribute( "SupportsMappingDimensions" ), AttributeExpression( "renderbackfaces", "F_RENDER_BACKFACES" ), Attribute( "ShadowFastPath" )]
public struct VertexColor : IInterpolatedCombinerSurface
{
	typealias Varyings = VertexColorPaint;

	[Group( "Color" ), Srgb, Opacity, Input( "TextureColor", "Color/10" ), Pack( "RGB: Color", "DXT1" ), TextureAttribute( "RepresentativeTexture" )] Texture2D Color;
	[Group( "Color,10/20" ), Color, Default3( 1, 1, 1 )] float3 ColorTint;
	[Group( "Color,10/30" ), Default( 1 ), Range( 0, 1 )] float ModelTintAmount;
	[Group( "Fade,400/10" ), Default( 1 ), Range( 1, 16 )] float FadeExponent;
	[Group( "Texture Coordinates,80/10" ), Default2( 1, 1 ), Range( 0, 100 )] float2 TexCoordScale;
	[Group( "Texture Coordinates/20" ), Range( -1, 1 )] float2 TexCoordOffset;
	[Group( "Texture Coordinates/30" ), Range( -10, 10 )] float2 TexCoordScrollSpeed;
	[Group( "Metalness,27/20" ), Range( 0, 1 )] float Metalness;
	[Group( "Fog,300" ), Default( 1 )] bool FogEnabled;
	[Group( "Ambient Occlusion,40" ), InputWith( "TextureAmbientOcclusion", "Ambient Occlusion,40/10", "Default3( 1.0, 1.0, 1.0 )" ), Pack( "R: Box3( AmbientOcclusion )", "ATI1N" )] Texture2D AmbientOcclusion;

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
