// eyeball.shader written as a surface: the cornea and sclera over the iris, each lit with its own normal, roughness and
// reflectance and mixed by the iris mask (diffuse light lerped, the iris's specular added under the cornea's), the sclera
// with wrapped diffuse, the iris shifted by exposure and parallax, self-illumination, and the eye-occlusion texture
// projected from the object's attribute over the whole lit colour. It lights itself through the engine's lighting
// (ILayeredSurface) with eyeball.shader's lighting features ([Combiner]), as eyeball.shader does.
import sbox.surface;
import eyeball_surface_layers;

[Material, Combiner( "S_SUBSURFACE_SCATTERING = SUBSURFACE_SCATTERING_WRAP, S_SPECULAR = 1" )]
public struct Eyeball : ILayeredSurface
{
	// Features
	[Group( "Features" ), Source( "F_SELF_ILLUM" )] bool UseSelfIllum;

	// Colour (its alpha is the iris's reflectance)
	[Group( "Color" ), Srgb] Texture2D Color;
	[Group( "Color" ), Default3( 1, 1, 1 )] float3 ColorTint;
	[Group( "Lighting" ), Default2( 0, 1 ), Range( 0, 1 )] float2 ReflectanceRange;

	// The cornea and sclera
	[Group( "Normal" )] Texture2D Normal;
	[Group( "Normal" ), Default( 1 ), Range( 0, 2 )] float CorneaScleraBumpStrength;
	[Group( "Normal" ), Default( 0.3 ), Range( 0, 1 )] float ScleraDiffuseWrap;
	[Group( "Normal" ), Default( 1.5 ), Range( 1, 2 )] float ScleraDiffuseExponent;

	// The iris: its mask, and its normal with roughness
	[Group( "Iris" )] Texture2D IrisMask;
	[Group( "Iris" )] Texture2D Iris;
	[Group( "Iris" ), Default( 1 ), Range( 0, 2 )] float IrisBumpStrength;
	[Group( "Iris" ), Default( 0.05 ), Range( 0, 0.2 )] float ParallaxStrength;
	[Group( "Iris" ), Default2( 0, 0 ), Range( 0, 1 )] float2 IrisAutoexposureRange;

	// Eye occlusion
	[Group( "Occlusion" )] Texture2D Occlusion;
	[Group( "Occlusion" ), Default( 3 ), Range( 0.1, 5 )] float OcclusionWidth;
	[Group( "Occlusion" ), Default( 0.75 ), Range( 0, 1 )] float OcclusionStrength;

	// Self-illumination
	[Group( "Self Illum" ), Srgb] Texture2D SelfIllumMask;
	[Group( "Self Illum" ), Default3( 1, 1, 1 )] float3 SelfIllumTint;
	[Group( "Self Illum" ), Default( 0 ), Range( -10, 10 )] float SelfIllumBrightness;
	[Group( "Self Illum" ), Default( 1 ), Range( 0, 1 )] float SelfIllumAlbedoFactor;

	/// vr_common.fxc's DecodeHemiOctahedronNormal with the tools' y flip, scaled by a bump strength as eyeball.shader does.
	static float3 Bumped( float2 hemiOctahedron, float strength )
	{
		float2 d = float2( hemiOctahedron.x + hemiOctahedron.y - 1.00392163, hemiOctahedron.x - hemiOctahedron.y );
		float3 n = normalize( float3( d, 1.0 - abs( d.x ) - abs( d.y ) ) );
		n.y = -n.y;
		n.xy *= strength;
		n.z = 1.0;
		return normalize( n );
	}

	EyeballLayers Layers( SurfaceInput i )
	{
		float2 uv = i.Uv;

		// The cornea's normal, and the iris shifted by the exposure and by parallax along the view
		float3 corneaTs = Bumped( i.Sample( Normal, uv ).ag, CorneaScleraBumpStrength );
		float irisMask = i.Sample( IrisMask, uv ).r;
		float2 lowExposureUv = uv - ( corneaTs.xy * irisMask * IrisAutoexposureRange.x );
		float2 highExposureUv = uv + ( corneaTs.xy * irisMask * IrisAutoexposureRange.y );
		float2 exposureUv = lerp( highExposureUv, lowExposureUv, saturate( i.ToneMapScale * 0.4 ) );
		float parallaxMask = i.Sample( IrisMask, exposureUv ).r;
		float3 cameraToPosition = -i.View;
		float2 viewTs = float2( dot( cameraToPosition, i.TangentU ), dot( cameraToPosition, i.TangentV ) );
		float2 shiftedUv = exposureUv + viewTs * parallaxMask * ParallaxStrength;

		// The iris's normal and anisotropic roughness
		float4 iris = i.Sample( Iris, shiftedUv );
		float4 color = i.Sample( Color, shiftedUv );
		float3 albedo = color.rgb * SrgbGammaToLinear( ColorTint ) * i.Color.rgb;

		EyeballLayers l;
		l.Surface = Material.Init( i );
		l.Surface.Normal = i.ToWorld( corneaTs );
		l.Surface.Roughness = 0.05;
		l.Surface.Albedo = albedo;
		l.Surface.Metalness = 0;
		l.Surface.Opacity = i.Color.a;
		l.IrisNormal = i.ToWorld( Bumped( iris.ag, IrisBumpStrength ) );
		l.IrisRoughness = iris.rb;
		l.IrisMask = irisMask;
		l.IrisReflectance = color.a * ( max( ReflectanceRange.x, ReflectanceRange.y ) - ReflectanceRange.x ) + ReflectanceRange.x;

		// The eye occlusion projected from the object's attribute (an affine projection: the same per pixel as
		// interpolated from the vertices)
		float2 occlusionUv = mul( g_matEyeballOcclusionWorldToTexture, float4( i.Position, 1.0 ) ).xy * float2( 0.5, -0.5 ) + float2( 0.5, 0.5 );
		float occlusion = i.Sample( Occlusion, occlusionUv, g_EyeballAnisoClamp ).r;
		l.Occlusion = pow( occlusion, OcclusionWidth ) * OcclusionStrength + ( 1.0 - OcclusionStrength );

		if ( UseSelfIllum )
		{
			float3 tint = pow( 2, SelfIllumBrightness ).xxx * SrgbGammaToLinear( SelfIllumTint );
			l.Surface.Emission = i.Sample( SelfIllumMask, uv ).rgb * tint * lerp( float3( 1, 1, 1 ), albedo, SelfIllumAlbedoFactor );
		}

		return l;
	}

	public Material Evaluate( SurfaceInput i )
	{
		return Layers( i ).Surface;
	}

	public float3 Shade<L : ILayerLighting>( SurfaceInput i, L lighting, out Material m )
	{
		EyeballLayers l = Layers( i );
		m = l.Surface;

		// The cornea and sclera, wrapped diffuse about the geometric normal, lit a tenth of a unit out along it
		LightLayer cornea = {};
		cornea.Normal = m.Normal;
		cornea.DiffuseNormal = i.Normal;
		cornea.Roughness = float2( 0.05, 0.05 );
		cornea.SpecularColor = float3( 0.04, 0.04, 0.04 );
		cornea.DiffuseWrap = ScleraDiffuseWrap;
		cornea.DiffuseWrapExponent = ScleraDiffuseExponent;
		cornea.Offset = 0.1 * i.Normal;
		LayerLight light = lighting.Light( cornea );

		// The iris under it: its diffuse light in place of the sclera's, its specular added under the cornea's
		[branch]
		if ( l.IrisMask > 0.0 )
		{
			LightLayer iris = cornea;
			iris.Normal = l.IrisNormal;
			iris.Roughness = l.IrisRoughness;
			iris.SpecularColor = l.IrisReflectance.xxx;
			iris.DiffuseWrap = 0.0;
			iris.DiffuseWrapExponent = 1.0;
			LayerLight irisLight = lighting.Light( iris );
			light.Diffuse = lerp( light.Diffuse, irisLight.Diffuse, l.IrisMask );
			light.IndirectDiffuse = lerp( light.IndirectDiffuse, irisLight.IndirectDiffuse, l.IrisMask );
			light.Specular += irisLight.Specular * l.IrisMask;
			light.IndirectSpecular += irisLight.IndirectSpecular * l.IrisMask;
		}

		float3 color = ( light.Diffuse + light.IndirectDiffuse ) * m.Albedo;
		color += m.Emission;
		color += light.Specular;
		color += light.IndirectSpecular;
		color *= l.Occlusion;
		return color;
	}
}
