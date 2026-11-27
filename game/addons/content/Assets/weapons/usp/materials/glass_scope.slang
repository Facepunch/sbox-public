// glass_scope: the USP's sight glass, a surface. glass.shader's refraction (the frame-buffer copy along the refracted ray,
// blurred by roughness and grazing angle, mixed into the emission by the environment reflection's share) with an
// iridescent tint, and the sight's dot: three layers of the RedDot texture read along the view ray at three depths behind
// the glass, so the dot holds still against the target as the weapon moves. F_RENDER_SIGHTS_ONLY draws the dot alone,
// unlit and alpha-blended, over whatever's behind (no frame-buffer copy). Draws in the translucent layer and writes depth
// as the .shader did; its shadows are its depth mode's dithered opacity (IShadowSurface).
import sbox.surface;

[Material, DepthWrite, Translucent, Features( "Common" ), Vertex( "Common" ), Attribute( "translucent" ),
	AttributeOf( "bWantsFBCopyTexture", "SightsOnly == 0" )]
public struct GlassScope : ICompositeSurface, IShadowSurface
{
	// The engine's, bound by attribute as the .shader's are
	[Engine( "FrameBufferCopyTexture" )] Texture2D ScopeFrameBufferCopy;
	[Engine( "BRDFLookup" )] Texture2D ScopeBrdfLookup;
	[Sampler( "Trilinear", "Mirror" )] SamplerState ScopeTrilinearMirror;
	[Sampler( "Trilinear", "Clamp" )] SamplerState ScopeTrilinearClamp;
	[Sampler( "Anisotropic", "Wrap" )] SamplerState ScopeSightFiltering;

	[Group( "Glass" ), Feature( "F_RENDER_SIGHTS_ONLY" ), RenderState( "Blend: Opaque, Alpha" )] bool SightsOnly;

	// The common inputs: colour with translucency, normal with tint mask, roughness/metalness/occlusion/blend
	[Group( "Material,10" ), Srgb, Opacity, TextureAttribute( "RepresentativeTexture" )] Texture2D Color;
	[Group( "Material,10" )] Texture2D Normal;
	[Group( "Material,10" )] Rma Rma;
	[Group( "Material,10/90" ), Source( "g_flTintColor" ), Color, Default3( 1, 1, 1 )] float3 TintColor;

	// Blur and refraction
	[Group( "Glass,10/10" ), Default( 0 ), Range( 0, 1 )] float BlurAmount;
	[Group( "Glass,10/20" ), Default( 1.005 ), Range( 1, 1.1 )] float RefractionStrength;
	[Group( "Glass,10/30" ), Default( 600 ), Range( 0, 1000 )] float Iridescence;
	[Group( "Glass,10/30" ), Default( 1 ), Range( 0, 10 )] float IridescenceScale;
	[Group( "Glass,10/40" ), Color, Default3( 1, 1, 1 )] float3 SightLightColor;
	[Group( "Glass,10/50" ), Default( 1 ), Range( 0, 20 )] float SightDistanceScale;
	[Group( "Fog,300" ), Default( 1 )] bool FogEnabled;

	// The sight dot: a layer per channel, each read at its own depth behind the glass
	[InputWith( "RedDot", "Sight Dot,10/10", "Srgb; _color; Default3( 0.0, 0.0, 0.0 )" ), InputWith( "RedDot2", "Sight Dot,10/10", "Srgb; _color; Default3( 0.0, 0.0, 0.0 )" ),
		InputWith( "RedDot3", "Sight Dot,10/10", "Srgb; _color; Default3( 0.0, 0.0, 0.0 )" ), Pack( "R: RedDot Linear; G: RedDot2 Linear; B: RedDot3 Linear", "BC7" )] Texture2D RedDot;

	/// common/BRDF.hlsl's CalcBRDFReflectionFactor: the environment's reflected share from the BRDF lookup.
	static float3 ReflectionFactor( float nDotV, float roughness, float3 specular )
	{
		float2 terms = ScopeBrdfLookup.SampleLevel( ScopeTrilinearClamp, float2( nDotV, roughness ), 0.0 ).rg;
		terms *= terms;
		return specular * terms.x + terms.y;
	}

	/// A thin film's colour at the view's angle, k its thickness in nanometres.
	static float3 GetIridescence( float3 cameraDir, float3 normal, float k )
	{
		const float3 c1 = float3( 3.54585104, 2.93225262, 2.41593945 );
		const float3 x1 = float3( 0.69549072, 0.49228336, 0.27699880 );
		const float3 y1 = float3( 0.02312639, 0.15225084, 0.52607955 );

		const float3 c2 = float3( 3.90307140, 3.21182957, 3.96587128 );
		const float3 x2 = float3( 0.11748627, 0.86755042, 0.66077860 );
		const float3 y2 = float3( 0.84897130, 0.88445281, 0.73949448 );

		float3 color = 0;
		float nDotV = dot( normal, cameraDir );

		[unroll]
		for ( int n = 1; n <= 8; n++ )
		{
			float wavelength = abs( nDotV ) * k / float( n );
			float x = saturate( ( wavelength - 400.0f ) / 300.0f );

			float3 col1 = saturate( 1 - ( c1 * ( x - x1 ) ) * ( c1 * ( x - x1 ) ) - y1 );
			float3 col2 = saturate( 1 - ( c2 * ( x - x2 ) ) * ( c2 * ( x - x2 ) ) - y2 );

			color += col1 + col2;
		}

		return color;
	}

	public Material Evaluate( SurfaceInput i )
	{
		Material m = Material.FromCommonInputs( i, Color, Normal, Rma, TintColor );
		m.Metalness = 0; // Glass is always non-metallic

		float3 ray = normalize( i.PositionFromOrigin );
		float nDotV = saturate( dot( -m.Normal, ray ) );
		float3 environment = ReflectionFactor( nDotV, m.Roughness, 0.04 );

		// Where the refracted ray meets the scene behind, on screen
		float3 behind = i.ScenePosition - i.Camera;
		float distanceVs = distance( i.PositionFromOrigin, behind );
		float3 refracted = i.PositionFromOrigin + refract( ray, m.Normal, 1.0 / RefractionStrength ) * distanceVs;
		float4 projected = mul( i.WorldToProjection, float4( refracted, 0 ) );
		float2 screen = projected.xy / projected.w * 0.5 + 0.5;
		screen.y = 1.0 - screen.y;

		float3 albedoTint = m.Albedo + GetIridescence( ray, m.Normal, Iridescence ) * IridescenceScale * m.Albedo;

		// Colour and blur: isotropic, by roughness and the grazing angle, through the copy's mips
		float amount = BlurAmount * m.Roughness * ( 1.0 - ( 1.0 / distanceVs ) );
		amount /= nDotV;
		float4 refraction = SightsOnly ? 0 : ScopeFrameBufferCopy.SampleLevel( ScopeTrilinearMirror, screen * i.FrameBufferUvScale, sqrt( amount ) * 7 );

		m.Emission = lerp( refraction.xyz, 0.0f, environment );
		m.Emission *= albedoTint;
		m.Albedo = 0;

		// Sight dot: each layer offset along the view ray's projection on the glass's tangents
		float3 lightColor = SightLightColor * 20.0f;
		float projectA = -SightDistanceScale / dot( ray, m.Normal );
		float2 along = float2( dot( ray, i.TangentU ), dot( ray, i.TangentV ) );
		float lod = sqrt( amount ) * 7;
		float dot1 = RedDot.SampleLevel( ScopeSightFiltering, saturate( i.Uv + ( projectA * 0.5 ) * along ), lod ).r;
		dot1 += RedDot.SampleLevel( ScopeSightFiltering, saturate( i.Uv + ( projectA * 1.1 ) * along ), lod ).g;
		dot1 += RedDot.SampleLevel( ScopeSightFiltering, saturate( i.Uv + ( projectA * 1.2 ) * along ), lod ).b;
		float4 dotColor = float4( dot1 * lightColor * albedoTint, dot1 );
		m.Emission = lerp( m.Emission, dotColor.rgb, dotColor.a );

		// The dot alone: unlit, its colour and coverage handed to Composite as they are
		if ( SightsOnly )
		{
			m.Unlit = true;
			m.Emission = dotColor.rgb;
			m.Opacity = dotColor.a;
		}

		return m;
	}

	// The dot alone replaces the lit colour, as the .shader returned it
	public float4 Composite( float4 shaded, Material m, SurfaceInput i )
	{
		if ( SightsOnly ) return float4( m.Emission, m.Opacity );
		return shaded;
	}

	// Depth and shadows, as the .shader's depth mode: the environment reflection at the view's angle (the geometric
	// normal's), made more opaque by roughness, refraction and albedo
	public float ShadowOpacity( SurfaceInput i )
	{
		Material m = Material.FromCommonInputs( i, Color, Normal, Rma, TintColor );
		float facing = dot( -i.Normal, i.CameraDirection );
		float opacity = ReflectionFactor( facing, m.Roughness, 0.04 ).x;
		opacity = pow( opacity, 1.0f / 2.0f );
		opacity = lerp( opacity, 0.75f, sqrt( m.Roughness ) );
		opacity = lerp( opacity, 1.0 - facing, ( RefractionStrength - 1.0f ) * 5.0f );
		return lerp( 1.0f, opacity + 0.04f, length( m.Albedo ) );
	}
}
