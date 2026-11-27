// water_surface's helpers: the body's render attributes, the clipmap, waves, detail normals, overlays, refraction and reflection
import sbox.surface;

// The body: its plane, extent and clipmap
[Engine( "Origin" )] float3 Origin;
[Engine( "AxisX" ), Default3( 1, 0, 0 )] float3 AxisX;
[Engine( "AxisY" ), Default3( 0, 1, 0 )] float3 AxisY;
[Engine( "SurfaceNormal" ), Default3( 0, 0, 1 )] float3 SurfaceNormal;
[Engine( "HalfSize" ), Default2( 1024, 1024 )] float2 HalfSize;
[Engine( "WaterOcean" ), Default( 0 )] bool WaterOcean;
[Engine( "GridCamera" )] float3 GridCamera;
[Engine( "ClipmapLevels" ), Default( 0 )] int ClipmapLevels;
[Engine( "ClipmapCells" ), Default( 64 )] int ClipmapCells;
[Engine( "ClipmapBaseCell" ), Default( 4 )] float ClipmapBaseCell;

// The waves, shared with water.shader (they read the plane above)
#include "sbox/water/waves.slangh"

// The waves' fade by footprint in the pixel stage, for the surface (a macro doesn't reach an importer)
static const float2 WaterPixelFade = WATER_PIXEL_FADE;

// What the WaterBody captures and how it reflects
[Engine( "HasRefractionTexture" ), Default( 0 )] bool HasRefractionTexture;
[Engine( "HasPlanarReflection" ), Default( 0 )] bool HasPlanarReflection;
[Engine( "ReflectionEnabled" ), Default( 1 )] bool ReflectionEnabled;
[Engine( "RefractionTexture" )] Texture2D RefractionTexture;
[Engine( "RefractionViewport" ), Default4( 0, 0, 1, 1 )] float4 RefractionViewport;
[Engine( "RefractionHasSceneFog" ), Default( 1 )] bool RefractionHasSceneFog;
[Engine( "PlanarReflectionTexture" )] Texture2D PlanarReflectionTexture;
[Engine( "PlanarReflectionDepthTexture" )] Texture2D PlanarReflectionDepthTexture;
[Engine( "PlanarReflectionPlane" )] float4 PlanarReflectionPlane;
[Engine( "PlanarReflectionProjectionToView" )] float4x4 PlanarReflectionProjectionToView;
[Engine( "PlanarReflectionDepthUvScaleBias" ), Default4( -1, 1, 1, 0 )] float4 PlanarReflectionDepthUvScaleBias;
[Engine( "PlanarReflectionViewport" ), Default4( 0, 0, 1, 1 )] float4 PlanarReflectionViewport;
[Engine( "PlanarReflectionDepthRange" ), Default2( 0, 1 )] float2 PlanarReflectionDepthRange;
[Engine( "ReflectionWaterHalfSize" )] float2 ReflectionWaterHalfSize;
[Engine( "ReflectionWaterInfinite" ), Default( 0 )] bool ReflectionWaterInfinite;

// Matches WaterBody.ReflectionMode
static const int ReflectionModePlanar = 1;
static const int ReflectionModeScreenSpace = 2;
[Engine( "ReflectionMode" ), Default( 2 )] int ReflectionMode;

// The environment a reflection falls back to: the scene's probes, or a sky cubemap of the body's own
[Engine( "HasEnvironment" ), Default( 0 )] bool HasEnvironment;
[Engine( "EnvironmentTextureIndex" )] int EnvironmentTextureIndex;
[Engine( "EnvironmentTextureFormat" ), Default( 0 )] int EnvironmentTextureFormat;
[Engine( "EnvironmentTint" ), Default3( 1, 1, 1 )] float3 EnvironmentTint;
[Engine( "EnvironmentAxisX" ), Default3( 1, 0, 0 )] float3 EnvironmentAxisX;
[Engine( "EnvironmentAxisY" ), Default3( 0, 1, 0 )] float3 EnvironmentAxisY;
[Engine( "EnvironmentAxisZ" ), Default3( 0, 0, 1 )] float3 EnvironmentAxisZ;

// Matches sky.shader's F_TEXTURE_FORMAT2; custom cubemaps stay linear HDR
static const int WaterSkyRgb = 1;
static const int WaterSkyYcocg = 2;
static const int WaterSkyRgbm = 4;
static const int WaterSkyRgbmu = 5;

// The overlays projected onto the surface (WaterOverlay.Data)
struct WaterOverlayData
{
	float4 U;
	float4 V;
	float4 Tint;
	float4 Settings;    // texture slot, roughness, normal distortion in inches, transmission
};

[Engine( "WaterOverlays" )] StructuredBuffer<WaterOverlayData> WaterOverlays;
[Engine( "WaterOverlayCount" ), Default( 0 )] int WaterOverlayCount;

// The detail normals' clock (the body's own, keeping the phase when the speed changes)
[Engine( "DetailTime" ), Default( 0 )] float DetailTime;

// The engine's BRDF lookup, for the environment's reflected share
[Engine( "BRDFLookup" )] Texture2D WaterBrdfLookup;

[Sampler( "Bilinear", "Clamp" )] SamplerState WaterBilinearClamp;
[Sampler( "Trilinear", "Clamp" )] SamplerState WaterTrilinearClamp;
[Sampler( "Anisotropic 8", "Wrap" )] SamplerState WaterDetailFiltering;
[Sampler( "Anisotropic 8", "Clamp" )] SamplerState WaterOverlayFiltering;

// The detail normals' fixed spectral recipe: the material chooses scale, speed and strength, not the layers' mechanics
static const float DetailWarp = 0.35;
static const float DetailPhaseScale = 0.4;
static const float3 DetailWeights = { 0.6, 0.35, 0.15 };
static const int DetailLayers = 3;
static const float2 DetailDirections[3] = { float2( 1, 0 ), float2( -0.5, 0.8660254 ), float2( -0.5, -0.8660254 ) };

// The clipmap: square levels of ClipmapCells cells around the camera, each with twice the cell size of the one inside
// it. A level snaps to twice its own cell size, so its vertices stay fixed in the world and every other one lands on the
// next coarser level's grid.
float2 WaterClipmapCamera() { return WaterLocal( GridCamera ).xy; }
float WaterClipmapCell( int level ) { return ClipmapBaseCell * exp2( level ); }

float2 WaterClipmapCenter( float cell, float2 camera )
{
	float snap = cell * 2;
	return round( camera / snap ) * snap;
}

// How far a point has blended towards the next coarser level: zero across most of a level, rising to one over its outer
// eighth, measured from the camera so it changes smoothly as the camera moves.
float WaterClipmapMorph( float2 point, float2 camera, float cell, int level )
{
	if ( level >= ClipmapLevels - 1 ) return 0;
	float2 distance = abs( point - camera ) / cell;
	float range = ClipmapCells / 8.0;
	return saturate( ( max( distance.x, distance.y ) - ( ClipmapCells * 0.5 - range ) ) / range );
}

// The mesh spacing wave filtering assumes at a point, matching the vertex stage's morphed footprint.
float WaterGridFootprintAtPoint( float2 point )
{
	if ( ClipmapLevels <= 0 ) return 0;
	float2 camera = WaterClipmapCamera();
	float2 offset = abs( point - camera );
	float reach = max( offset.x, offset.y ) / ( ClipmapCells * 0.5 * ClipmapBaseCell );
	int level = clamp( (int)ceil( log2( max( reach, 0.000001 ) ) ), 0, max( ClipmapLevels - 1, 0 ) );
	float cell = WaterClipmapCell( level );
	return cell * ( 1 + WaterClipmapMorph( point, camera, cell, level ) );
}

/// Exact dielectric Fresnel, with the critical angle and total internal reflection from below.
float WaterFresnel( float cosine, bool underwater )
{
	float eta = underwater ? 1.333 : 1 / 1.333;
	float sinTransmitted2 = eta * eta * ( 1 - cosine * cosine );
	if ( sinTransmitted2 >= 1 ) return 1;
	float transmitted = sqrt( 1 - sinTransmitted2 );
	float rs = ( eta * cosine - transmitted ) / max( eta * cosine + transmitted, 0.0001 );
	float rp = ( eta * transmitted - cosine ) / max( eta * transmitted + cosine, 0.0001 );
	return saturate( ( rs * rs + rp * rp ) * 0.5 );
}

/// Towards the eye along the active projection (parallel in an orthographic one).
float3 WaterSurfaceToEye( SurfaceInput i, float3 position )
{
	return i.Orthographic ? -normalize( i.CameraDirection ) : normalize( i.Camera - position );
}

/// The view's planar reflection is this water's when it was rendered for the plane the water lies on.
bool WaterHasPlanarReflection()
{
	return HasPlanarReflection && dot( PlanarReflectionPlane.xyz, SurfaceNormal ) > 0.9999 && abs( dot( Origin, PlanarReflectionPlane.xyz ) - PlanarReflectionPlane.w ) <= 1;
}

/// Where the planar capture saw something at a colour uv, in its view space (its inverse reverse-Z projection); false for
/// sky and outside the capture.
bool WaterPlanarViewPosition( float2 colorUv, out float3 position )
{
	position = 0;
	if ( any( colorUv < 0 ) || any( colorUv > 1 ) ) return false;

	float2 depthUv = colorUv * PlanarReflectionDepthUvScaleBias.xy + PlanarReflectionDepthUvScaleBias.zw;
	float2 viewportUv = ( depthUv - PlanarReflectionViewport.xy ) / PlanarReflectionViewport.zw;
	if ( any( viewportUv < 0 ) || any( viewportUv > 1 ) ) return false;

	uint width, height;
	PlanarReflectionDepthTexture.GetDimensions( width, height );
	int2 pixel = clamp( int2( depthUv * float2( width, height ) ), int2( 0, 0 ), int2( width - 1, height - 1 ) );
	float depth = saturate( ( PlanarReflectionDepthTexture.Load( int3( pixel, 0 ) ).r - PlanarReflectionDepthRange.x ) / ( PlanarReflectionDepthRange.y - PlanarReflectionDepthRange.x ) );
	if ( depth <= 0 ) return false;

	// At the texel centre, so silhouettes don't get a false depth slope
	float2 sampleUv = ( float2( pixel ) + 0.5 ) / float2( width, height );
	sampleUv = ( sampleUv - PlanarReflectionViewport.xy ) / PlanarReflectionViewport.zw;
	float4 point = mul( float4( sampleUv * float2( 2, -2 ) + float2( -1, 1 ), depth, 1 ), PlanarReflectionProjectionToView );
	if ( point.w <= 0.000001 ) return false;
	position = point.xyz / point.w;
	return true;
}

/// How much of the reflected path lies beyond the surface at a capture uv: zero at contact, one at infinity.
float WaterPlanarDistortionWeight( float2 colorUv, float surfaceDepth )
{
	float3 point;
	if ( !WaterPlanarViewPosition( colorUv, point ) ) return 1;
	return saturate( 1 - surfaceDepth / max( -point.z, 0.0001 ) );
}

/// The planar capture's uv for the wave's reflected ray: the capture mirrors the camera through the mean plane, so the ray is
/// reflected through it again; its depth anchors nearby reflections to the surface.
bool WaterPlanarReflectionUv<L : ISurfaceLighting>( L lighting, float3 position, float3 view, float3 normal, out float2 uv )
{
	float3 reflected = reflect( -view, normal );
	float3 captureDirection = reflect( reflected, SurfaceNormal );
	float4 projected = lighting.ProjectDirection( captureDirection );
	uv = 0;

	// A steep wave can turn the ray behind the capture: the fallback takes it
	if ( projected.w <= 0.0001 ) return false;

	uv = projected.xy / projected.w * float2( 0.5, -0.5 ) + 0.5;
	if ( HasPlanarReflection )
	{
		float3 mirroredPosition = position - 2 * dot( position - Origin, SurfaceNormal ) * SurfaceNormal;
		float4 surface = lighting.ProjectPosition( mirroredPosition );
		if ( surface.w > 0.0001 )
		{
			float2 baseUv = surface.xy / surface.w * float2( 0.5, -0.5 ) + 0.5;
			float weight = WaterPlanarDistortionWeight( baseUv, surface.w );
			float2 candidateUv = lerp( baseUv, uv, weight );

			// The nearer constraint across a silhouette: a sky sample mustn't restore full distortion under a nearby object
			weight = min( weight, WaterPlanarDistortionWeight( candidateUv, surface.w ) );
			uv = lerp( baseUv, uv, weight );
		}
	}

	return true;
}

/// Whether what the capture saw at a uv is above the water there (submerged hits fade out by half an inch).
float WaterPlanarVisibility<L : ISurfaceLighting>( L lighting, float3 camera, float2 uv )
{
	float3 point;
	if ( !HasPlanarReflection || !WaterPlanarViewPosition( uv, point ) ) return 1;

	float3 mirrored = camera + lighting.ViewToWorld( point * float3( -1, 1, 1 ) );
	float3 world = mirrored - 2 * dot( mirrored - Origin, SurfaceNormal ) * SurfaceNormal;
	float3 local = WaterLocal( world );
	if ( !ReflectionWaterInfinite && any( abs( local.xy ) > ReflectionWaterHalfSize ) ) return 1;

	// Most hits lie outside the wave envelope and need no wave solve
	if ( local.z >= WaveHeight - 0.125 ) return 1;
	if ( local.z <= -WaveHeight - 0.5 ) return 0;
	float height = WaveHeight > 0 ? WaterHeightLowerBound( local.xy, WaterGridFootprintAtPoint( local.xy ) ) : 0;
	return smoothstep( -0.5, -0.125, local.z - height );
}

/// The planar capture filtered over its valid texels in premultiplied form, so submerged colours can't bleed across
/// silhouettes; alpha is the coverage that survived, which the environment fills.
float4 SampleWaterPlanarReflection<L : ISurfaceLighting>( L lighting, float3 camera, float2 uv )
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
			float weight = ( x == 0 ? 1 - fraction.x : fraction.x ) * ( y == 0 ? 1 - fraction.y : fraction.y );
			float2 sampleUv = ( float2( pixel ) + 0.5 ) / float2( width, height );
			float coverage = step( 0.5, color.a ) * WaterPlanarVisibility( lighting, camera, sampleUv );
			result += float4( color.rgb * coverage, coverage ) * weight;
		}
	}

	return result;
}

/// The reflected environment: the scene's probes, or the body's own sky cubemap (rotated, its encoding decoded, tinted).
float3 SampleWaterEnvironment<L : ISurfaceLighting>( L lighting, float3 view, float3 normal, float roughness )
{
	if ( !HasEnvironment ) return lighting.Environment( normal, roughness );

	float3 ray = reflect( -view, normal );
	float3 direction = float3( dot( ray, EnvironmentAxisX ), dot( ray, EnvironmentAxisY ), dot( ray, EnvironmentAxisZ ) );
	uint width, height, levels;
	BindlessTextureCube( uint( EnvironmentTextureIndex ) ).GetDimensions( 0, width, height, levels );

	// The engine's GGX-prefiltered mip selection
	float mip = sqrt( saturate( roughness ) ) * levels;
	bool srgb = EnvironmentTextureFormat >= WaterSkyRgb && EnvironmentTextureFormat <= WaterSkyRgbmu;
	float4 texel = BindlessTextureCube( uint( EnvironmentTextureIndex + ( srgb ? 1 : 0 ) ) ).SampleLevel( WaterTrilinearClamp, direction, mip );
	float3 color = texel.rgb;
	if ( EnvironmentTextureFormat == WaterSkyYcocg )
	{
		float scale = texel.b * ( 255.0 / 8.0 ) + 1;
		float co = ( texel.r - 0.5 * 256.0 / 255.0 ) / scale;
		float cg = ( texel.g - 0.5 * 256.0 / 255.0 ) / scale;
		color = SrgbGammaToLinear( float3( texel.a + co - cg, texel.a + cg, texel.a - co - cg ) );
	}
	else if ( EnvironmentTextureFormat == WaterSkyRgbm || EnvironmentTextureFormat == WaterSkyRgbmu )
	{
		float3 linear = texel.rgb * 6.0 * texel.a;
		color = linear * linear;
	}

	return max( color * EnvironmentTint, 0 );
}

/// The overlays over a point, accumulated premultiplied in their authored order; roughness follows their coverage.
/// Raster passes only: each overlay's gradients are its warped uv's own.
float4 SampleWaterOverlays( float2 point, float3 normal, inout float roughness )
{
	float4 result = 0;
	float2 normalOffset = float2( dot( normal, AxisX ), dot( normal, AxisY ) );
	for ( int index = 0; index < WaterOverlayCount; index++ )
	{
		WaterOverlayData overlay = WaterOverlays[index];
		float2 uv = float2( dot( overlay.U.xy, point ) + overlay.U.z, dot( overlay.V.xy, point ) + overlay.V.z );
		float2 offset = normalOffset * overlay.Settings.z;
		float2 warped = uv + float2( dot( overlay.U.xy, offset ), dot( overlay.V.xy, offset ) );

		// Derivatives before the divergent footprint rejection
		float2 dx = ddx( warped );
		float2 dy = ddy( warped );
		if ( any( uv < 0 ) || any( uv > 1 ) || any( warped < 0 ) || any( warped > 1 ) ) continue;

		float4 texel = BindlessTexture2D( uint( (int)overlay.Settings.x + 1 ) ).SampleGrad( WaterOverlayFiltering, warped, dx, dy );
		float coverage = saturate( texel.a * overlay.Tint.a );
		float opacity = coverage * ( 1 - overlay.Settings.w );
		result.rgb = lerp( result.rgb, texel.rgb * overlay.Tint.rgb, opacity );
		result.a = opacity + result.a * ( 1 - opacity );
		roughness = lerp( roughness, overlay.Settings.y, coverage );
	}

	return result;
}
