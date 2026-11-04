#ifndef WATER_LIGHTING_HLSL
#define WATER_LIGHTING_HLSL

#include "common/classes/Fog.hlsl"

bool RefractionHasSceneFog < Attribute( "RefractionHasSceneFog" ); Default( 1 ); >;

// Matches WaterBody.ReflectionMode.
#define REFLECTION_MODE_PLANAR 1
#define REFLECTION_MODE_SCREEN_SPACE 2

bool HasEnvironment < Attribute( "HasEnvironment" ); Default( 0 ); >;
int ReflectionMode < Attribute( "ReflectionMode" ); Default( REFLECTION_MODE_SCREEN_SPACE ); >;
int EnvironmentTextureIndex < Attribute( "EnvironmentTextureIndex" ); >;
int EnvironmentTextureFormat < Attribute( "EnvironmentTextureFormat" ); Default( 0 ); >;

// Matches sky.shader's F_TEXTURE_FORMAT2. Custom cubemaps remain linear HDR.
#define WATER_SKY_RGB 1
#define WATER_SKY_YCOCG 2
#define WATER_SKY_RGBM 4
#define WATER_SKY_RGBMU 5
float3 EnvironmentTint < Attribute( "EnvironmentTint" ); Default3( 1, 1, 1 ); >;
float3 EnvironmentAxisX < Attribute( "EnvironmentAxisX" ); Default3( 1, 0, 0 ); >;
float3 EnvironmentAxisY < Attribute( "EnvironmentAxisY" ); Default3( 0, 1, 0 ); >;
float3 EnvironmentAxisZ < Attribute( "EnvironmentAxisZ" ); Default3( 0, 0, 1 ); >;

// Use the active projection, including child views, rather than a selected-camera flag.
// Engine probe lighting still has its own perspective view helpers.
float3 WaterSurfaceToEye( float3 position )
{
	if ( g_matViewToProjection[3].w != 0 )
		return -normalize( g_vCameraDirWs.xyz );

	return normalize( g_vCameraPositionWs - position );
}

// Read the selected environment independently of local probe priority and bounds.
// Skybox rotation is world-to-local for the reflected direction, with no translation.
float3 SampleWaterEnvironment( Material m, float3 view, float3 normal, float roughness )
{
	if ( !HasEnvironment )
		return EnvMap::From( m.WorldPosition, m.ScreenPosition, normal, float2( roughness, roughness ) );

	float3 ray = reflect( -view, normal );
	float3 direction = float3( dot( ray, EnvironmentAxisX ), dot( ray, EnvironmentAxisY ), dot( ray, EnvironmentAxisZ ) );
	TextureCube source = Bindless::GetTextureCube( UniformIndex( EnvironmentTextureIndex ) );
	uint width, height, levels;
	source.GetDimensions( 0, width, height, levels );
	// Match the engine's GGX-prefiltered environment mip selection.
	float mip = sqrt( saturate( roughness ) ) * levels;
	bool srgb = EnvironmentTextureFormat >= WATER_SKY_RGB && EnvironmentTextureFormat <= WATER_SKY_RGBMU;
	float4 texel = Bindless::GetTextureCube( UniformIndex( EnvironmentTextureIndex + (srgb ? 1 : 0) ) )
		.SampleLevel( g_sTrilinearClamp, direction, mip );
	float3 color = texel.rgb;
	if ( EnvironmentTextureFormat == WATER_SKY_YCOCG )
	{
		float scale = texel.b * (255.0 / 8.0) + 1;
		float co = (texel.r - 0.5 * 256.0 / 255.0) / scale;
		float cg = (texel.g - 0.5 * 256.0 / 255.0) / scale;
		color = SrgbGammaToLinear( float3( texel.a + co - cg, texel.a + cg, texel.a - co - cg ) );
	}
	else if ( EnvironmentTextureFormat == WATER_SKY_RGBM || EnvironmentTextureFormat == WATER_SKY_RGBMU )
	{
		color = RGBMDecode( texel );
	}
	return max( color * EnvironmentTint, 0 );
}

float4 PlanarReflectionPlane < Attribute( "PlanarReflectionPlane" ); >;
float4x4 PlanarReflectionProjectionToView < Attribute( "PlanarReflectionProjectionToView" ); >;
float4 PlanarReflectionDepthUvScaleBias < Attribute( "PlanarReflectionDepthUvScaleBias" ); Default4( -1, 1, 1, 0 ); >;
float4 PlanarReflectionViewport < Attribute( "PlanarReflectionViewport" ); Default4( 0, 0, 1, 1 ); >;
float2 PlanarReflectionDepthRange < Attribute( "PlanarReflectionDepthRange" ); Default2( 0, 1 ); >;
float2 ReflectionWaterHalfSize < Attribute( "ReflectionWaterHalfSize" ); >;
bool ReflectionWaterInfinite < Attribute( "ReflectionWaterInfinite" ); Default( 0 ); >;

// The view's planar reflection is this water's when it was rendered for the plane the water lies on.
bool WaterHasPlanarReflection()
{
	return HasPlanarReflection && dot( PlanarReflectionPlane.xyz, SurfaceNormal ) > 0.9999
		&& abs( dot( Origin, PlanarReflectionPlane.xyz ) - PlanarReflectionPlane.w ) <= 1;
}

// Both ordinary and oblique captures supply their actual inverse reverse-Z projection.
// Sky (including the separate 3D-sky depth range) has no main-world position to reject.
bool WaterPlanarViewPosition( float2 colorUv, out float3 position )
{
	position = 0;
	if ( any( colorUv < 0 ) || any( colorUv > 1 ) )
		return false;

	float2 depthUv = colorUv * PlanarReflectionDepthUvScaleBias.xy + PlanarReflectionDepthUvScaleBias.zw;
	float2 viewportUv = (depthUv - PlanarReflectionViewport.xy) / PlanarReflectionViewport.zw;
	if ( any( viewportUv < 0 ) || any( viewportUv > 1 ) )
		return false;

	uint width, height;
	PlanarReflectionDepthTexture.GetDimensions( width, height );
	int2 pixel = clamp( int2( depthUv * float2( width, height ) ), int2( 0, 0 ), int2( width - 1, height - 1 ) );
	float depth = saturate( (PlanarReflectionDepthTexture.Load( int3( pixel, 0 ) ).r - PlanarReflectionDepthRange.x)
		/ (PlanarReflectionDepthRange.y - PlanarReflectionDepthRange.x) );
	// Sky and the separate 3D-sky depth range retain the distant angular lookup.
	if ( depth <= 0 )
		return false;

	// Reconstruct at the sampled texel centre, avoiding a false depth slope at silhouettes.
	float2 sampleUv = (float2( pixel ) + 0.5) / float2( width, height );
	sampleUv = (sampleUv - PlanarReflectionViewport.xy) / PlanarReflectionViewport.zw;
	// The captured inverse projection already includes native reverse Z.
	// Matrix attributes upload raw rows into HLSL's column-major storage.
	float4 point = mul( float4( sampleUv * float2( 2, -2 ) + float2( -1, 1 ), depth, 1 ), PlanarReflectionProjectionToView );
	if ( point.w <= 0.000001 )
		return false;

	position = point.xyz / point.w;
	return true;
}

float WaterPlanarDistortionWeight( float2 colorUv, float surfaceDepth )
{
	float3 point;
	if ( !WaterPlanarViewPosition( colorUv, point ) )
		return 1;

	float hitDepth = -point.z;
	// Screen displacement scales by the fraction of the reflected path beyond the surface.
	// At contact it is zero; at infinite distance it approaches the full normal response.
	return saturate( 1 - surfaceDepth / max( hitDepth, 0.0001 ) );
}

// The capture mirrors the camera through the mean plane. Reflect the wave's outgoing
// ray through that plane again to express it in the original camera's projection.
// A flat normal cancels the two reflections and reproduces the undistorted view.
// Captured depth anchors nearby reflections to the surface instead of treating every hit as sky.
bool WaterPlanarReflectionUv( float3 position, float3 view, float3 normal, out float2 uv )
{
	float3 reflected = reflect( -view, normal );
	float3 captureDirection = reflect( reflected, SurfaceNormal );
	float4 projected = Position4WsToPs( float4( captureDirection, 0 ) );
	uv = 0;

	// A steep wave can turn the ray behind the capture. Let the normal reflection
	// fallback handle it instead of dividing through zero or sampling the wrong side.
	if ( projected.w <= 0.0001 )
		return false;

	uv = projected.xy / projected.w * float2( 0.5, -0.5 ) + 0.5;
	if ( HasPlanarReflection )
	{
		float3 mirroredPosition = position - 2 * dot( position - Origin, SurfaceNormal ) * SurfaceNormal;
		float4 surface = Position4WsToPs( float4( mirroredPosition, 1 ) );
		if ( surface.w > 0.0001 )
		{
			float2 baseUv = surface.xy / surface.w * float2( 0.5, -0.5 ) + 0.5;
			float weight = WaterPlanarDistortionWeight( baseUv, surface.w );
			float2 candidateUv = lerp( baseUv, uv, weight );
			// Keep the nearer constraint when the candidate crosses a silhouette. A sky
			// sample must not suddenly restore full distortion underneath a nearby object.
			weight = min( weight, WaterPlanarDistortionWeight( candidateUv, surface.w ) );
			uv = lerp( baseUv, uv, weight );
		}
	}

	return true;
}

float WaterPlanarVisibility( float2 uv )
{
	float3 point;
	if ( !HasPlanarReflection || !WaterPlanarViewPosition( uv, point ) )
		return 1;

	float3 mirrored = g_vCameraPositionWs + Vector3VsToWs( point * float3( -1, 1, 1 ) );
	float3 world = mirrored - 2 * dot( mirrored - Origin, SurfaceNormal ) * SurfaceNormal;
	float3 local = WaterLocal( world );
	if ( !ReflectionWaterInfinite && any( abs( local.xy ) > ReflectionWaterHalfSize ) )
		return 1;

	// Most hits lie outside the wave envelope and need no wave solve. Keep a small
	// contact tolerance and fade out by half an inch beneath the surface.
	if ( local.z >= WaveHeight - 0.125 )
		return 1;
	if ( local.z <= -WaveHeight - 0.5 )
		return 0;

	float height = WaveHeight > 0 ? WaterHeightLowerBound( local.xy, WaterGridFootprintAtPoint( local.xy ) ) : 0;
	return smoothstep( -0.5, -0.125, local.z - height );
}

// Filter valid texels in premultiplied form, so submerged colours cannot bleed across
// silhouettes. Alpha becomes surviving capture coverage; the environment fills its holes.
float4 SampleWaterPlanarReflection( float2 uv )
{
	uint width, height, levels;
	PlanarReflectionTexture.GetDimensions( 0, width, height, levels );
	float2 halfTexel = 0.5 / float2( width, height );
	uv = clamp( uv, halfTexel, 1 - halfTexel );
	float2 texel = uv * float2( width, height ) - 0.5;
	int2 basePixel = int2( floor( texel ) );
	float2 fraction = frac( texel );
	float4 result = 0;
	[unroll]
	for ( int y = 0; y < 2; y++ )
	{
		[unroll]
		for ( int x = 0; x < 2; x++ )
		{
			int2 pixel = clamp( basePixel + int2( x, y ), int2( 0, 0 ), int2( width - 1, height - 1 ) );
			float4 color = PlanarReflectionTexture.Load( int3( pixel, 0 ) );
			float weight = (x == 0 ? 1 - fraction.x : fraction.x) * (y == 0 ? 1 - fraction.y : fraction.y);
			float2 sampleUv = (float2( pixel ) + 0.5) / float2( width, height );
			float coverage = step( 0.5, color.a ) * WaterPlanarVisibility( sampleUv );
			result += float4( color.rgb * coverage, coverage ) * weight;
		}
	}

	return result;
}

// Keep engine lights, shadows, AO and fog while using the selected water reflection source.
float4 WaterShade( Material m, CombinerInput input, float3 reflection, float3 capturedReflection, float capturedReflectionWeight, float fresnel, float3 transmitted, float transmittedCoverage, bool underwater )
{
	// Water starts at two percent reflectance, independent of its surface colour.
	input.vSpecularColor = float3( 0.02037, 0.02037, 0.02037 );
	LightingTerms_t lighting = InitLightingTerms();
	ComputeDirectLighting( lighting, input );
	CalculateIndirectLighting( lighting, input );
	float3 diffuseAo = CalculateDiffuseAmbientOcclusion( input, lighting );
	float3 specularAo = CalculateSpecularAmbientOcclusion( input, lighting );
	float3 diffuse = (lighting.vDiffuse.rgb * lerp( 1, diffuseAo, input.flAmbientOcclusionDirectDiffuse )
		+ lighting.vIndirectDiffuse.rgb * diffuseAo) * input.vDiffuseColor.rgb;
	float3 directSpecular = underwater ? 0 : lighting.vSpecular.rgb * lerp( 1, specularAo, input.flAmbientOcclusionDirectSpecular );
	float3 selectedFactor = 0;
	if ( ReflectionEnabled )
	{
		// Both sources use the same rough-surface BRDF and occlusion. Correct the engine's
		// Schlick Fresnel to water's dielectric Fresnel,
		// without discarding the BRDF's roughness-dependent energy response.
		float cosine = saturate( dot( WaterSurfaceToEye( m.WorldPosition ), m.Normal ) );
		float grazing = pow( 1 - cosine, 5 );
		float waterSchlick = 0.02037 + (1 - 0.02037) * grazing;
		float fresnelCorrection = fresnel / max( waterSchlick, 0.0001 );

		// The engine BRDF LUT's first column loses energy at low roughness and NdotV = 0.
		// Start at the second texel's centre so filtering cannot pull in that singular edge.
		// Only the lookup angle changes; Fresnel and reflection directions keep the real angle.
		uint brdfWidth, brdfHeight;
		BRDFLookup.GetDimensions( brdfWidth, brdfHeight );
		float lookupCosine = max( cosine, 1.5 / brdfWidth );
		float3 reflectionFactor = CalcBRDFReflectionFactor( lookupCosine, IsotropicRoughnessFromAnisotropicRoughness( input.vRoughness ), input.vSpecularColor );
		selectedFactor = reflectionFactor * fresnelCorrection * specularAo;
	}

	float3 indirectSpecular = reflection * selectedFactor;
	// Emissive decals remain visible without scene lights.
	float4 color = float4( diffuse + directSpecular + indirectSpecular + transmitted + m.Emission, 1 );
	if ( g_bWireframeMode )
		return g_vWireframeColor;
	if ( ToolsVis::WantsToolsVis() )
		return ShadingModelStandard::DoToolsVis( color, m, lighting );
	if ( !g_bFogEnabled )
		return color;

	// Water fog replaces the capture's scene atmosphere. Apply the viewer's analytic fog
	// only to radiance and coverage that do not already contain it; fog is affine (A*C+B).
	float3 reflectedCapture = capturedReflection * selectedFactor;
	float3 reflectedCoverage = capturedReflectionWeight * selectedFactor;
	float3 remote = transmitted + reflectedCapture;
	float3 remoteCoverage = transmittedCoverage + reflectedCoverage;
	float3 foggedRemote = (RefractionHasSceneFog ? transmitted : 0) + reflectedCapture;
	float3 foggedCoverage = (RefractionHasSceneFog ? transmittedCoverage : 0) + reflectedCoverage;
	float3 scatter = Fog::Apply( m.WorldPosition, 0 );
	float3 transmission = Fog::Apply( m.WorldPosition, 1 ) - scatter;
	float3 foggedColor = transmission * (color.rgb - foggedRemote)
		+ scatter * (1 - foggedCoverage) + foggedRemote;

	// Both captures already contain volumetric fog, independently of their analytic override.
	float3 volumeRemote = foggedRemote + transmission * (remote - foggedRemote);
	float3 volumeCoverage = foggedCoverage + transmission * (remoteCoverage - foggedCoverage);
	float3 volumeScatter = ApplyVolumetricFog( 0, m.WorldPosition, m.ScreenPosition.xy );
	float3 volumeLocal = ApplyVolumetricFog( foggedColor - volumeRemote, m.WorldPosition, m.ScreenPosition.xy );
	return float4( volumeLocal + volumeRemote - volumeScatter * volumeCoverage, 1 );
}

#endif
