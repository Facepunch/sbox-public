HEADER
{
	Description = "Lit water surface with waves, reflection and refraction";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_CAUSTICS, 0..1, "Caustics" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#define S_SPECULAR 1
	#include "common/shared.hlsl"
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
	#include "water/waves.hlsl"

	// The wave mesh is a clipmap: square levels of ClipmapCells cells around the camera, each with twice
	// the cell size of the one inside it. A level snaps to twice its own cell size, so its vertices stay
	// fixed in the world and every other one lands on the next coarser level's grid.

	float2 WaterClipmapCamera()
	{
		return WaterLocal( GridCamera ).xy;
	}

	float WaterClipmapCell( int level )
	{
		return ClipmapBaseCell * exp2( level );
	}

	float2 WaterClipmapCenter( float cell, float2 camera )
	{
		float snap = cell * 2;
		return round( camera / snap ) * snap;
	}

	// How far a point has blended towards the next coarser level. Zero across most of a level, rising to one
	// over its outer eighth. The coarser level's hole always starts at least ClipmapCells / 2 cells out, so
	// the blend is complete wherever the two levels meet. Measured from the camera, not the snapped centres,
	// so it changes smoothly as the camera moves.
	float WaterClipmapMorph( float2 point, float2 camera, float cell, int level )
	{
		if ( level >= ClipmapLevels - 1 )
			return 0;

		float2 distance = abs( point - camera ) / cell;
		float range = ClipmapCells / 8.0;
		return saturate( (max( distance.x, distance.y ) - (ClipmapCells * 0.5 - range)) / range );
	}

	// Mesh spacing that wave filtering assumes at a point, matching the vertex shader's morphed footprint.
	float WaterGridFootprintAtPoint( float2 point )
	{
		if ( ClipmapLevels <= 0 )
			return 0;

		float2 camera = WaterClipmapCamera();
		float2 offset = abs( point - camera );
		float reach = max( offset.x, offset.y ) / (ClipmapCells * 0.5 * ClipmapBaseCell);
		int level = clamp( (int)ceil( log2( max( reach, 0.000001 ) ) ), 0, max( ClipmapLevels - 1, 0 ) );
		float cell = WaterClipmapCell( level );
		return cell * (1 + WaterClipmapMorph( point, camera, cell, level ));
	}
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float2 waterPoint : TEXCOORD8;
	float2 waterRest : TEXCOORD9;
	// One on the underside mesh: the same surface with its winding flipped, drawn for the faces looking down.
	nointerpolation float waterUnderside : TEXCOORD10;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		o.waterUnderside = i.vTexCoord2.y;

		if ( ClipmapLevels > 0 )
		{
			int level = (int)i.vTexCoord2.x;
			float2 camera = WaterClipmapCamera();
			float cell = WaterClipmapCell( level );
			float2 center = WaterClipmapCenter( cell, camera );
			float2 p = center + i.vTexCoord.xy * cell;

			// Near the outer edge, slide odd vertices onto their even neighbours. At the edge this level then
			// has the coarser level's vertices, and samples waves with its footprint, so the two meet without cracks.
			float morph = WaterClipmapMorph( p, camera, cell, level );
			float2 index = round( center / cell ) + i.vTexCoord.xy;
			p -= (index - 2 * floor( index * 0.5 )) * cell * morph;
			float footprint = cell * (1 + morph);

			// Fill exactly the coarser level's hole: vertices past its edge collapse onto it, leaving
			// zero-area cells. Finite water also stops at its walls.
			float2 lower = -1e30;
			float2 upper = 1e30;
			if ( level < ClipmapLevels - 1 )
			{
				float parentCell = cell * 2;
				float2 parentCenter = WaterClipmapCenter( parentCell, camera );
				float hole = (ClipmapCells / 4 + 1) * parentCell;
				lower = parentCenter - hole;
				upper = parentCenter + hole;
			}
			if ( !WaterOcean )
			{
				lower = max( lower, -HalfSize );
				upper = min( upper, HalfSize );
			}

			// A level that misses a finite body entirely collapses to one point, drawing nothing.
			p = any( lower > upper ) ? clamp( camera, -HalfSize, HalfSize ) : clamp( p, lower, upper );

			float3 offset;
			float2 slope;
			WaterWaves( p, footprint, offset, slope );
			if ( !WaterOcean )
			{
				// A finite pool samples the same wave field up to its authored walls.
				// Keep wall vertices on the wall and clip displaced interior vertices to it.
				float2 displaced = p + offset.xy;
				float2 bounded = clamp( displaced, -HalfSize, HalfSize );
				bounded = lerp( bounded, p, step( HalfSize, abs( p ) ) );
				if ( any( bounded != displaced ) )
				{
					p = WaterRestPoint( bounded, footprint, offset, slope );
					offset.xy = bounded - p;
				}
			}

			o.vPositionWs = WaterWorld( float3( p + offset.xy, offset.z ) );
			o.vPositionPs = Position3WsToPs( o.vPositionWs );
			o.vNormalWs = normalize( SurfaceNormal - AxisX * slope.x - AxisY * slope.y );
			o.waterPoint = p + offset.xy;
			o.waterRest = p;
		}
		else
		{
			// Flat water retains the two-triangle footprint.
			o.waterPoint = WaterLocal( o.vPositionWs ).xy;
			o.waterRest = o.waterPoint;
		}

		return FinalizeVertex( o );
	}
}

PS
{
	// Use per-pixel DDGI/IBL instead of choosing one baked probe volume for the entire surface.
	#define NEEDS_LIGHT_PROBE false
	#define S_DIFFUSE_TWO_SIDED 1
	#include "common/pixel.hlsl"
	#include "water/detail.hlsl"
	#include "water/overlays.hlsl"

	// A WaterBody draws the surface twice, the underside with flipped winding, so each draw shows one side.
	RenderState( CullMode, BACK );

	bool HasRefractionTexture < Attribute( "HasRefractionTexture" ); Default( 0 ); >;
	bool HasPlanarReflection < Attribute( "HasPlanarReflection" ); Default( 0 ); >;
	bool ReflectionEnabled < Attribute( "ReflectionEnabled" ); Default( 1 ); >;
	Texture2D RefractionTexture < Attribute( "RefractionTexture" ); SrgbRead( false ); >;
	float4 RefractionViewport < Attribute( "RefractionViewport" ); Default4( 0, 0, 1, 1 ); >;
	Texture2D PlanarReflectionTexture < Attribute( "PlanarReflectionTexture" ); SrgbRead( false ); >;
	Texture2D PlanarReflectionDepthTexture < Attribute( "PlanarReflectionDepthTexture" ); SrgbRead( false ); >;
	// Lit colour, and how much of it covers the captured scene behind: zero transmits it, one is opaque.
	float3 SurfaceColor < UiType( Color ); Default3( 0.01, 0.171, 0.262 ); UiGroup( "Surface,10/10" ); >;
	float Opacity < Default( 0.5 ); Range( 0, 1 ); UiGroup( "Surface,10/20" ); >;
	float g_fRoughness < Default( 0.02 ); Range( 0, 1 ); UiGroup( "Surface,10/30" ); >;

	// The same, seen from below.
	float3 UndersideColor < UiType( Color ); Default3( 0.01, 0.171, 0.262 ); UiGroup( "Underside,50/10" ); >;
	float UndersideOpacity < Default( 0 ); Range( 0, 1 ); UiGroup( "Underside,50/20" ); >;

	// Distortion of the captured scene by the surface normals.
	float RefractionStrength < Default( 1 ); Range( 0, 10 ); UiGroup( "Refraction,30/10" ); >;

	// Fog seen looking down through the surface, which the WaterBody renders into its refraction capture.
	// Below the surface the body's underwater fog applies instead, starting from this colour.
	// Depth range: depths below the surface where the fog starts and where it's full. Depth curve: how the fog
	// builds between them. Attenuation distance: with distance fog on, how far through the water removes about
	// 63 percent of the light; smaller values give denser fog.
	float3 WaterFogColor < UiType( Color ); Default3( 0.01, 0.171, 0.262 ); UiGroup( "Through-Surface Fog,35/10" ); >;
	float2 WaterFogDepthRange < Default2( 0, 128 ); Range2( 0, 0, 4096, 4096 ); UiGroup( "Through-Surface Fog,35/20" ); >;
	float WaterFogDepthCurve < Default( 0.5 ); Range( 0.1, 4 ); UiGroup( "Through-Surface Fog,35/30" ); >;
	bool WaterFogDistance < Default( 0 ); UiGroup( "Through-Surface Fog,35/40" ); >;
	float WaterFogAttenuationDistance < Default( 128 ); Range( 1, 4096 ); UiGroup( "Through-Surface Fog,35/50" ); >;

	// Caustics the WaterBody projects onto what's below, animated with the detail normals. Only the CPU reads them;
	// the texture attribute keeps the texture in the compiled material.
	CreateInputTexture2D( Caustics, Linear, 8, "", "", "Caustics,40/10", Default3( 1, 1, 1 ) );
	Texture2D g_tCaustics < Channel( RGB, Box( Caustics ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	TextureAttribute( WaterCaustics, g_tCaustics );
	float CausticsIntensity < Default( 1 ); Range( 0, 8 ); UiGroup( "Caustics,40/15" ); >;
	float CausticsBrightness < Default( 4 ); Range( 0, 16 ); UiGroup( "Caustics,40/20" ); >;
	float CausticsContrast < Default( 1 ); Range( 0, 8 ); UiGroup( "Caustics,40/30" ); >;

	#include "water/lighting.hlsl"

	// Exact dielectric Fresnel, including the critical angle and total internal reflection from below.
	float WaterFresnel( float cosine, bool underwater )
	{
		float eta = underwater ? 1.333 : 1 / 1.333;
		float sinTransmitted2 = eta * eta * (1 - cosine * cosine);
		if ( sinTransmitted2 >= 1 )
			return 1;
		float transmitted = sqrt( 1 - sinTransmitted2 );
		float rs = (eta * cosine - transmitted) / max( eta * cosine + transmitted, 0.0001 );
		float rp = (eta * transmitted - cosine) / max( eta * transmitted + cosine, 0.0001 );
		return saturate( (rs * rs + rp * rp) * 0.5 );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );
		bool underwater = i.waterUnderside > 0.5;
		float3 view = WaterSurfaceToEye( m.WorldPosition );
		float2 point = i.waterPoint;
		float3 waveOffset;
		float2 gradient;
		float3 jacobian;
		WaterWaves( i.waterRest, length( fwidth( i.waterRest ) ), WATER_PIXEL_FADE, waveOffset, gradient, jacobian );
		gradient += WaterBumpSlope( point );
		float3 normal = normalize( SurfaceNormal - AxisX * gradient.x - AxisY * gradient.y );
		float2 distortion = Vector3WsToVs( normal - SurfaceNormal ).xy * float2( 0.1, -0.1 );
		m.Normal = underwater ? -normal : normal;
		m.Roughness = max( 0.001, g_fRoughness );
		float4 overlay = SampleWaterOverlays( point, normal, m.Roughness );
		if ( DepthNormals::WantsDepthNormals() )
			return DepthNormals::Output( m.Normal, m.Roughness, 1 );

		float2 uv = CalculateViewportUv( i.vPositionSs.xy );
		float4 undersideReflection = 0;
		if ( underwater && ReflectionEnabled )
			undersideReflection = DynamicReflections::Sample( i.vPositionSs.xy - g_vViewportOffset, sqrt( saturate( m.Roughness ) ) );
		float fresnel = WaterFresnel( saturate( dot( m.Normal, view ) ), underwater );
		float reflectedCoverage = ReflectionEnabled ? fresnel * (underwater ? saturate( undersideReflection.a ) : 1) : 0;
		float transmission = HasRefractionTexture ? 1 - saturate( underwater ? UndersideOpacity : Opacity ) : 0;
		float3 refraction = 0;
		if ( HasRefractionTexture )
		{
			float2 refractUv = clamp( uv + distortion * RefractionStrength * (underwater ? 2.0 : 0.25), 0.001, 0.999 );
			refraction = RefractionTexture.SampleLevel( g_sBilinearClamp,
				RefractionViewport.xy + refractUv * RefractionViewport.zw, 0 ).rgb;
		}
		float3 coveredColor = (underwater ? UndersideColor : SurfaceColor) * (1 - transmission) * (1 - overlay.a) + overlay.rgb;
		transmission *= 1 - overlay.a;
		m.Albedo = coveredColor * (1 - reflectedCoverage);
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		float transmittedCoverage = transmission * (1 - reflectedCoverage);
		float3 transmitted = refraction * transmittedCoverage;

		Decals::Apply( m.WorldPosition, m );
		m.Roughness = max( 0.001, m.Roughness );
		CombinerInput lightingInput = ShadingModelStandard::MaterialToCombinerInput( m );
		float3 capturedReflection = 0;
		float captureWeight = 0;
		float3 reflection = 0;
		if ( ReflectionEnabled )
		{
			// Planar water whose view rendered no reflection for its plane, because another plane covers more of
			// the screen or this one too little, uses screen-space reflections like any other water.
			if ( underwater || ReflectionMode != REFLECTION_MODE_PLANAR || !WaterHasPlanarReflection() )
			{
				float4 screen = underwater ? undersideReflection
					: DynamicReflections::Sample( i.vPositionSs.xy - g_vViewportOffset, sqrt( saturate( m.Roughness ) ) );
				captureWeight = saturate( screen.a );
				capturedReflection = screen.rgb * captureWeight;
			}
			else
			{
				float2 reflectUv;
				if ( WaterPlanarReflectionUv( m.WorldPosition, view, m.Normal, reflectUv ) )
				{
					float4 planar = SampleWaterPlanarReflection( reflectUv );
					captureWeight = planar.a;
					capturedReflection = planar.rgb;
				}
			}
			reflection = capturedReflection;
			if ( !underwater && captureWeight < 1 )
				reflection += SampleWaterEnvironment( m, view, m.Normal, m.Roughness ) * (1 - captureWeight);
		}
		return WaterShade( m, lightingInput, reflection, capturedReflection, captureWeight, fresnel, transmitted, transmittedCoverage, underwater );
	}
}
