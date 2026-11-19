// water.shader written as a surface that owns its vertex stage and shades itself (IVertexStageSurface,
// IShadedVertexStageSurface). The vertex stage places the clipmap's levels around the camera, each snapped to twice its
// cell and clamped into the next level's hole and a finite body's walls, and moves them by the Gerstner waves. The pixel
// stage takes the wave normal at the rest position (filtered by its footprint, from the Varyings' derivatives) plus the
// animated detail normals and overlays, refracts the body's capture of the scene, reflects the planar capture, the
// screen-space reflections or the environment, and lights the rest through the engine's lighting with water's own 2%
// specular and dielectric Fresnel; it fogs what it didn't capture already fogged. The WaterBody's render attributes drive
// it, as they drive the .shader.
import sbox.surface;

// The body: its plane, extent and clipmap
float3 Origin < Attribute( "Origin" ); >;
float3 AxisX < Attribute( "AxisX" ); Default3( 1, 0, 0 ); >;
float3 AxisY < Attribute( "AxisY" ); Default3( 0, 1, 0 ); >;
float3 SurfaceNormal < Attribute( "SurfaceNormal" ); Default3( 0, 0, 1 ); >;
float2 HalfSize < Attribute( "HalfSize" ); Default2( 1024, 1024 ); >;
bool WaterOcean < Attribute( "WaterOcean" ); Default( 0 ); >;
float3 GridCamera < Attribute( "GridCamera" ); >;
int ClipmapLevels < Attribute( "ClipmapLevels" ); Default( 0 ); >;
int ClipmapCells < Attribute( "ClipmapCells" ); Default( 64 ); >;
float ClipmapBaseCell < Attribute( "ClipmapBaseCell" ); Default( 4 ); >;

// The waves, shared with water.shader (they read the plane above)
#include "water/waves.hlsl"

// What the WaterBody captures and how it reflects
bool HasRefractionTexture < Attribute( "HasRefractionTexture" ); Default( 0 ); >;
bool HasPlanarReflection < Attribute( "HasPlanarReflection" ); Default( 0 ); >;
bool ReflectionEnabled < Attribute( "ReflectionEnabled" ); Default( 1 ); >;
Texture2D RefractionTexture < Attribute( "RefractionTexture" ); SrgbRead( false ); >;
float4 RefractionViewport < Attribute( "RefractionViewport" ); Default4( 0, 0, 1, 1 ); >;
bool RefractionHasSceneFog < Attribute( "RefractionHasSceneFog" ); Default( 1 ); >;
Texture2D PlanarReflectionTexture < Attribute( "PlanarReflectionTexture" ); SrgbRead( false ); >;
Texture2D PlanarReflectionDepthTexture < Attribute( "PlanarReflectionDepthTexture" ); SrgbRead( false ); >;
float4 PlanarReflectionPlane < Attribute( "PlanarReflectionPlane" ); >;
float4x4 PlanarReflectionProjectionToView < Attribute( "PlanarReflectionProjectionToView" ); >;
float4 PlanarReflectionDepthUvScaleBias < Attribute( "PlanarReflectionDepthUvScaleBias" ); Default4( -1, 1, 1, 0 ); >;
float4 PlanarReflectionViewport < Attribute( "PlanarReflectionViewport" ); Default4( 0, 0, 1, 1 ); >;
float2 PlanarReflectionDepthRange < Attribute( "PlanarReflectionDepthRange" ); Default2( 0, 1 ); >;
float2 ReflectionWaterHalfSize < Attribute( "ReflectionWaterHalfSize" ); >;
bool ReflectionWaterInfinite < Attribute( "ReflectionWaterInfinite" ); Default( 0 ); >;

// Matches WaterBody.ReflectionMode
static const int ReflectionModePlanar = 1;
static const int ReflectionModeScreenSpace = 2;
int ReflectionMode < Attribute( "ReflectionMode" ); Default( 2 ); >;

// The environment a reflection falls back to: the scene's probes, or a sky cubemap of the body's own
bool HasEnvironment < Attribute( "HasEnvironment" ); Default( 0 ); >;
int EnvironmentTextureIndex < Attribute( "EnvironmentTextureIndex" ); >;
int EnvironmentTextureFormat < Attribute( "EnvironmentTextureFormat" ); Default( 0 ); >;
float3 EnvironmentTint < Attribute( "EnvironmentTint" ); Default3( 1, 1, 1 ); >;
float3 EnvironmentAxisX < Attribute( "EnvironmentAxisX" ); Default3( 1, 0, 0 ); >;
float3 EnvironmentAxisY < Attribute( "EnvironmentAxisY" ); Default3( 0, 1, 0 ); >;
float3 EnvironmentAxisZ < Attribute( "EnvironmentAxisZ" ); Default3( 0, 0, 1 ); >;

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

StructuredBuffer<WaterOverlayData> WaterOverlays < Attribute( "WaterOverlays" ); >;
int WaterOverlayCount < Attribute( "WaterOverlayCount" ); Default( 0 ); >;

// The detail normals' clock (the body's own, keeping the phase when the speed changes)
float DetailTime < Attribute( "DetailTime" ); Default( 0 ); >;

// The engine's BRDF lookup, for the environment's reflected share
Texture2D WaterBrdfLookup < Attribute( "BRDFLookup" ); >;

SamplerState WaterBilinearClamp < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
SamplerState WaterTrilinearClamp < Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
SamplerState WaterDetailFiltering < Filter( ANISOTROPIC ); MaxAniso( 8 ); >;
SamplerState WaterOverlayFiltering < Filter( ANISOTROPIC ); MaxAniso( 8 ); AddressU( CLAMP ); AddressV( CLAMP ); >;

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

/// What the water's vertex stage reads of its clipmap mesh: a level's cell coordinates, and its level and side.
public struct WaterVertex
{
	float3 Position : POSITION;
	float2 Cell : TEXCOORD0;     // within its level, from the level's centre
	float2 Level : TEXCOORD1;    // x: the clipmap level; y: one on the underside mesh (the same surface, winding flipped)
}

/// What it hands the pixel stage: the displaced and rest points on the surface, and the side.
public struct WaterVaryings
{
	float2 Point;
	float2 Rest;
	nointerpolation float Underside;
}

[Material, Lighting( "S_DIFFUSE_TWO_SIDED" )]
public struct Water : IVertexStageSurface, IShadedVertexStageSurface
{
	public typealias VertexInput = WaterVertex;
	public typealias Varyings = WaterVaryings;

	// Lit colour, and how much of it covers the captured scene behind: zero transmits it, one is opaque
	[Group( "Surface" ), Default3( 0.01, 0.171, 0.262 )] float3 SurfaceColor;
	[Group( "Surface" ), Range( 0, 1 ), Default( 0.5 )] float Opacity;
	[Group( "Surface" ), Range( 0, 1 ), Default( 0.02 ), Source( "g_fRoughness" )] float Roughness;

	// The same, seen from below
	[Group( "Underside" ), Default3( 0.01, 0.171, 0.262 )] float3 UndersideColor;
	[Group( "Underside" ), Range( 0, 1 ), Default( 0 )] float UndersideOpacity;

	// Distortion of the captured scene by the surface normals
	[Group( "Refraction" ), Range( 0, 10 ), Default( 1 )] float RefractionStrength;

	// Animated detail normals (linear tangent-space; blue is normal z): the largest layer's width in inches and their
	// strength in lighting and distortion. They don't displace the geometry
	[Group( "Detail" )] Texture2D DetailNormal;
	[Group( "Detail" ), Range( 1, 4096 ), Default( 376.988 )] float DetailScale;
	[Group( "Detail" ), Range( 0, 4 ), Default( 0.156 )] float DetailStrength;

	public SurfaceVertex Vertex( WaterVertex v, VertexStageInput s, out WaterVaryings varyings )
	{
		SurfaceVertex o = {};
		varyings.Underside = v.Level.y;
		float3 meshNormal = v.Level.y > 0.5 ? -SurfaceNormal : SurfaceNormal;
		o.TangentU = AxisX;
		o.TangentV = cross( meshNormal, AxisX );
		o.Uv = v.Cell;

		if ( ClipmapLevels > 0 )
		{
			int level = (int)v.Level.x;
			float2 camera = WaterClipmapCamera();
			float cell = WaterClipmapCell( level );
			float2 center = WaterClipmapCenter( cell, camera );
			float2 p = center + v.Cell * cell;

			// Near the outer edge, odd vertices slide onto their even neighbours: at the edge this level has the coarser
			// level's vertices, and samples waves with its footprint, so the two meet without cracks
			float morph = WaterClipmapMorph( p, camera, cell, level );
			float2 index = round( center / cell ) + v.Cell;
			p -= ( index - 2 * floor( index * 0.5 ) ) * cell * morph;
			float footprint = cell * ( 1 + morph );

			// Exactly the coarser level's hole: vertices past its edge collapse onto it. Finite water stops at its walls
			float2 lower = -1e30;
			float2 upper = 1e30;
			if ( level < ClipmapLevels - 1 )
			{
				float parentCell = cell * 2;
				float2 parentCenter = WaterClipmapCenter( parentCell, camera );
				float hole = ( ClipmapCells / 4 + 1 ) * parentCell;
				lower = parentCenter - hole;
				upper = parentCenter + hole;
			}
			if ( !WaterOcean )
			{
				lower = max( lower, -HalfSize );
				upper = min( upper, HalfSize );
			}

			// A level past ClipmapLevels (the mesh has room for more), or one that misses a finite body entirely,
			// collapses to one point, drawing nothing
			p = level >= ClipmapLevels || any( lower > upper ) ? clamp( camera, -HalfSize, HalfSize ) : clamp( p, lower, upper );

			float3 offset;
			float2 slope;
			WaterWaves( p, footprint, offset, slope );
			if ( !WaterOcean )
			{
				// A finite pool samples the same wave field up to its walls: wall vertices stay on the wall and displaced
				// interior vertices are clipped to it
				float2 displaced = p + offset.xy;
				float2 bounded = clamp( displaced, -HalfSize, HalfSize );
				bounded = lerp( bounded, p, step( HalfSize, abs( p ) ) );
				if ( any( bounded != displaced ) )
				{
					p = WaterRestPoint( bounded, footprint, offset, slope );
					offset.xy = bounded - p;
				}
			}

			o.Position = WaterWorld( float3( p + offset.xy, offset.z ) );
			o.Normal = normalize( SurfaceNormal - AxisX * slope.x - AxisY * slope.y );
			varyings.Point = p + offset.xy;
			varyings.Rest = p;
		}
		else
		{
			// Flat water keeps its two-triangle footprint
			o.Position = mul( s.ObjectToWorld, float4( v.Position, 1 ) );
			o.Normal = meshNormal;
			varyings.Point = WaterLocal( o.Position ).xy;
			varyings.Rest = varyings.Point;
		}

		return o;
	}

	/// The animated detail normals' slope at a point: each band blends two equally weighted normals moving against each
	/// other on three fixed axes; smaller bands scroll more slowly in world space.
	float2 BumpSlope( SurfaceInput i, float2 point, float2 dx, float2 dy )
	{
		float strength = clamp( DetailStrength, 0, 4 );
		if ( !( strength > 0 ) ) return 0;

		float scale = clamp( DetailScale, 1, 4096 );
		float2 phaseOffset = 0;
		float2 slope = 0;
		float totalWeight = 0;
		[unroll]
		for ( int layer = 0; layer < DetailLayers; layer++ )
		{
			float weight = DetailWeights[layer];
			float2 uv = ( point + phaseOffset ) / scale;
			float2 travel = DetailDirections[layer] * DetailTime / sqrt( scale );
			float2 first = DetailNormal.SampleGrad( WaterDetailFiltering, uv + travel, dx / scale, dy / scale ).rg - 0.5;

			// Distortion small enough not to fold the dense texture features; the phases apart so the samples differ at zero
			float2 warp = first * ( DetailWarp * 0.1 * weight );
			float2 second = DetailNormal.SampleGrad( WaterDetailFiltering, uv - travel + float2( 0.37, 0.61 ) + warp, dx / scale, dy / scale ).rg - 0.5;
			slope += ( first + second ) * weight;
			phaseOffset += warp * scale * weight * DetailPhaseScale;
			totalWeight += weight;
			scale *= DetailPhaseScale;
		}

		return slope * ( strength * 0.3 ) / max( totalWeight, 0.001 );
	}

	/// The surface's normal at the pixel: the waves at the rest point, filtered by its footprint, and the detail normals.
	float3 WaveNormal( SurfaceInput i, WaterVaryings v, Derivatives<WaterVaryings> d )
	{
		float3 waveOffset;
		float2 gradient;
		float3 jacobian;
		WaterWaves( v.Rest, length( abs( d.Ddx.Rest ) + abs( d.Ddy.Rest ) ), WATER_PIXEL_FADE, waveOffset, gradient, jacobian );
		gradient += BumpSlope( i, v.Point, d.Ddx.Point, d.Ddy.Point );
		return normalize( SurfaceNormal - AxisX * gradient.x - AxisY * gradient.y );
	}

	/// Depth passes (only alpha-tested ones read it): the surface's normal and roughness.
	public Material Evaluate( SurfaceInput i, WaterVaryings v, Derivatives<WaterVaryings> d )
	{
		Material m = Material.Init( i );
		float3 normal = WaveNormal( i, v, d );
		m.Normal = v.Underside > 0.5 ? -normal : normal;
		m.Roughness = max( 0.001, Roughness );
		return m;
	}

	public float4 Shade<L : ISurfaceLighting>( SurfaceInput i, WaterVaryings v, Derivatives<WaterVaryings> d, L lighting, out Material m )
	{
		bool underwater = v.Underside > 0.5;
		float3 normal = WaveNormal( i, v, d );
		m = Material.Init( i );
		m.Normal = underwater ? -normal : normal;
		m.Roughness = max( 0.001, Roughness );
		float4 overlay = SampleWaterOverlays( v.Point, normal, m.Roughness );

		float3 position = i.Position;
		float3 view = WaterSurfaceToEye( i, position );
		float2 distortion = lighting.WorldToView( normal - SurfaceNormal ).xy * float2( 0.1, -0.1 );
		float2 uv = lighting.ViewportUv();

		float4 undersideReflection = 0;
		if ( underwater && ReflectionEnabled ) undersideReflection = lighting.ScreenReflection( m.Roughness );
		float fresnel = WaterFresnel( saturate( dot( m.Normal, view ) ), underwater );
		float reflectedCoverage = ReflectionEnabled ? fresnel * ( underwater ? saturate( undersideReflection.a ) : 1 ) : 0;
		float transmission = HasRefractionTexture ? 1 - saturate( underwater ? UndersideOpacity : Opacity ) : 0;
		float3 refraction = 0;
		if ( HasRefractionTexture )
		{
			float2 refractUv = clamp( uv + distortion * RefractionStrength * ( underwater ? 2.0 : 0.25 ), 0.001, 0.999 );
			refraction = RefractionTexture.SampleLevel( WaterBilinearClamp, RefractionViewport.xy + refractUv * RefractionViewport.zw, 0 ).rgb;
		}

		float3 coveredColor = ( underwater ? UndersideColor : SurfaceColor ) * ( 1 - transmission ) * ( 1 - overlay.a ) + overlay.rgb;
		transmission *= 1 - overlay.a;
		float transmittedCoverage = transmission * ( 1 - reflectedCoverage );
		float3 transmitted = refraction * transmittedCoverage;

		// Lit as the engine lights it, decals included, with water's two percent reflectance whatever its colour
		Material lit = m;
		lit.Albedo = coveredColor * ( 1 - reflectedCoverage );
		lit.Metalness = 0;
		lit.AmbientOcclusion = 1;
		lit = lighting.ApplyDecals( lit );
		lit.Roughness = max( 0.001, lit.Roughness );
		SurfaceLight light = lighting.LightMaterialWithSpecular( lit, float3( 0.02037, 0.02037, 0.02037 ) );

		// The reflection: the planar capture, else screen-space reflections, with the environment where they found nothing
		float3 capturedReflection = 0;
		float captureWeight = 0;
		float3 reflection = 0;
		if ( ReflectionEnabled )
		{
			// Planar water whose view rendered no reflection for its plane uses screen-space reflections like any other
			if ( underwater || ReflectionMode != ReflectionModePlanar || !WaterHasPlanarReflection() )
			{
				float4 screen = underwater ? undersideReflection : lighting.ScreenReflection( lit.Roughness );
				captureWeight = saturate( screen.a );
				capturedReflection = screen.rgb * captureWeight;
			}
			else
			{
				float2 reflectUv;
				if ( WaterPlanarReflectionUv( lighting, position, view, lit.Normal, reflectUv ) )
				{
					float4 planar = SampleWaterPlanarReflection( lighting, i.Camera, reflectUv );
					captureWeight = planar.a;
					capturedReflection = planar.rgb;
				}
			}

			reflection = capturedReflection;
			if ( !underwater && captureWeight < 1 ) reflection += SampleWaterEnvironment( lighting, view, lit.Normal, lit.Roughness ) * ( 1 - captureWeight );
		}

		float3 diffuse = ( light.Diffuse * lerp( 1, light.DiffuseOcclusion, light.DirectDiffuseOcclusion ) + light.IndirectDiffuse * light.DiffuseOcclusion ) * light.DiffuseColor;
		float3 directSpecular = underwater ? 0 : light.Specular * lerp( 1, light.SpecularOcclusion, light.DirectSpecularOcclusion );
		float3 selectedFactor = 0;
		if ( ReflectionEnabled )
		{
			// Both sources use the same rough-surface BRDF and occlusion: the engine's Schlick Fresnel corrected to water's
			// dielectric one. The BRDF lookup starts at its second texel's centre, past the singular edge at NdotV = 0
			float cosine = saturate( dot( view, lit.Normal ) );
			float grazing = pow( 1 - cosine, 5 );
			float waterSchlick = 0.02037 + ( 1 - 0.02037 ) * grazing;
			float fresnelCorrection = fresnel / max( waterSchlick, 0.0001 );
			uint brdfWidth, brdfHeight;
			WaterBrdfLookup.GetDimensions( brdfWidth, brdfHeight );
			float lookupCosine = max( cosine, 1.5 / brdfWidth );
			float2 terms = WaterBrdfLookup.SampleLevel( WaterTrilinearClamp, float2( lookupCosine, dot( light.Roughness, float2( 0.5, 0.5 ) ) ), 0.0 ).rg;
			terms *= terms;
			selectedFactor = ( light.SpecularColor * terms.x + terms.y ) * fresnelCorrection * light.SpecularOcclusion;
		}

		// Emissive decals stay visible without scene lights
		float3 indirectSpecular = reflection * selectedFactor;
		float3 color = diffuse + directSpecular + indirectSpecular + transmitted + lit.Emission;
		if ( !lighting.FogEnabled() ) return float4( color, 1 );

		// Water fog replaces the capture's scene atmosphere: the viewer's analytic fog applies only to radiance and coverage
		// that don't already contain it (fog is affine, A*C+B)
		float3 reflectedCapture = capturedReflection * selectedFactor;
		float3 reflectedCoverageColor = captureWeight * selectedFactor;
		float3 remote = transmitted + reflectedCapture;
		float3 remoteCoverage = transmittedCoverage + reflectedCoverageColor;
		float3 foggedRemote = ( RefractionHasSceneFog ? transmitted : 0 ) + reflectedCapture;
		float3 foggedCoverage = ( RefractionHasSceneFog ? transmittedCoverage : 0 ) + reflectedCoverageColor;
		float3 scatter = lighting.Fog( 0 );
		float3 fogTransmission = lighting.Fog( 1 ) - scatter;
		float3 foggedColor = fogTransmission * ( color - foggedRemote ) + scatter * ( 1 - foggedCoverage ) + foggedRemote;

		// Both captures already contain volumetric fog, whatever their analytic override
		float3 volumeRemote = foggedRemote + fogTransmission * ( remote - foggedRemote );
		float3 volumeCoverage = foggedCoverage + fogTransmission * ( remoteCoverage - foggedCoverage );
		float3 volumeScatter = lighting.VolumetricFog( 0 );
		float3 volumeLocal = lighting.VolumetricFog( foggedColor - volumeRemote );
		return float4( volumeLocal + volumeRemote - volumeScatter * volumeCoverage, 1 );
	}
}
