// water.shader written as a surface that owns its vertex stage and shades itself (IVertexStageSurface,
// IShadedVertexStageSurface). The vertex stage places the clipmap's levels around the camera, each snapped to twice its
// cell and clamped into the next level's hole and a finite body's walls, and moves them by the Gerstner waves. The pixel
// stage takes the wave normal at the rest position (filtered by its footprint, from the Varyings' derivatives) plus the
// animated detail normals and overlays, refracts the body's capture of the scene, reflects the planar capture, the
// screen-space reflections or the environment, and lights the rest through the engine's lighting with water's own 2%
// specular and dielectric Fresnel; it fogs what it didn't capture already fogged. The WaterBody's render attributes drive
// it, as they drive the .shader.
import sbox.surface;
import water_surface_shading;

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
		WaterWaves( v.Rest, length( abs( d.Ddx.Rest ) + abs( d.Ddy.Rest ) ), WaterPixelFade, waveOffset, gradient, jacobian );
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
