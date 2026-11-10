// Procedural citizen eyes. No authored colour, normal, mask or noise textures.
HEADER
{
	Description = "Citizen procedural eye";
	DevShader = true;
	Version = 48;
	CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#include "system.fxc"
	#define S_SUBSURFACE_SCATTERING SUBSURFACE_SCATTERING_WRAP
	#define S_SPECULAR 1
	#define S_SPECULAR_CUBE_MAP 1
	#include "vr_common.fxc"
}

struct VS_INPUT
{
	#include "vr_shared_standard_vs_input.fxc"
};

struct PS_INPUT
{
	#include "vr_shared_standard_ps_input.fxc"
	float3 vEyeForwardWs : TEXCOORD14;
	float3 vEyeRightWs : TEXCOORD15;
};

VS
{
	#include "vr_common_vs_code.fxc"
	DynamicCombo( D_BAKED_LIGHTING_FROM_LIGHTMAP, 0..1, Sys( ALL ) );

	PS_INPUT MainVs( VS_INPUT i )
	{
		PS_INPUT o = VS_CommonProcessing( i );

		// Carry the eye's bind-pose +X axis through the existing tangent frame.
		// This follows skinning/gaze without bone indices or eye-centre parameters.
		float3 normalOs;
		float4 tangentOs;
		VS_DecodeObjectSpaceNormalAndTangent( i, normalOs, tangentOs );
		float3 bitangentOs = cross( normalOs, tangentOs.xyz ) * tangentOs.w;
		o.vEyeForwardWs = o.vTangentUWs.xyz * tangentOs.x
			+ o.vTangentVWs.xyz * bitangentOs.x + o.vNormalWs.xyz * normalOs.x;
		o.vEyeRightWs = o.vTangentUWs.xyz * tangentOs.y
			+ o.vTangentVWs.xyz * bitangentOs.y + o.vNormalWs.xyz * normalOs.y;

		return VS_CommonProcessing_Post( o );
	}
}

PS
{
	#include "vr_common_ps_code.fxc"
	#include "common/classes/EnvMap.hlsl"
	#include "common/classes/Depth.hlsl"
	#include "procedural.hlsl"
	DynamicCombo( D_OPAQUE_FADE, 0..1, Sys( ALL ) );

	// The cornea has one optical model: derive its normal-incidence reflectance
	// from the same fixed refractive index used for the viewing ray.
	static const float EyeCorneaIor = 1.376;
	static const float EyeCorneaFresnelRatio = (EyeCorneaIor - 1) / (EyeCorneaIor + 1);
	static const float EyeCorneaReflectance = EyeCorneaFresnelRatio * EyeCorneaFresnelRatio;

	// Identity and mesh fit are the only artist controls. Optical and tissue
	// properties below are calibrated together, rather than independently tuned.
	float3 g_vIrisColor < UiType( Color ); Default3( 0.28, 0.19, 0.11 ); UiGroup( "Iris,10/10" ); >;
	float g_flDetailSeed < Default( 1 ); Range( 0, 100 ); UiGroup( "Iris,10/20" ); >;
	float g_flPupilSize < Default( 0.30 ); Range( 0.15, 0.75 ); UiGroup( "Iris,10/30" ); >;
	float g_flIrisRadius < Default( 0.20 ); Range( 0.10, 0.30 ); UiGroup( "Mesh Fit,20/10" ); >;
	float2 g_vIrisCenter < Default2( 0.5, 0.5 ); Range2( 0.35, 0.35, 0.65, 0.65 ); UiGroup( "Mesh Fit,20/20" ); >;

	// Shared UV scale for projection, refraction and contact occlusion.
	static const float EyeUvRadius = 0.49101;

	// Human iris anatomy, in iris-radius units. The narrow transition belongs to
	// the limbus; antialiasing adds only the pixel footprint at viewing distance.
	static const float EyeEdgeSoftness = 0.085;
	static const float EyeFiberStrength = 0.55;
	static const float EyeFiberCount = 96;
	static const float EyeRingStrength = 0.12;
	static const float EyeMicroFiberStrength = 0.5;
	static const float EyeCryptStrength = 0.65;
	static const float EyeIrisRelief = 0.26;
	static const float EyeLimbalWidth = 0.09;
	static const float EyeLimbalStrength = 0.82;
	static const float EyePupilRimStrength = 0.65;

	// A lightly pigmented sclera under the same wet tear film as the cornea.
	static const float3 EyeScleraColor = { 0.69, 0.685, 0.67 };
	static const float3 EyeScleraEdgeColor = { 0.58, 0.37, 0.34 };
	static const float EyeScleraRedness = 0.45;
	static const float EyeScleraDiffuseWrap = 0.3;
	static const float EyeVeinStrength = 0.4;
	// Intrinsic microfacet roughness; pixel-footprint filtering is added below.
	static const float EyeCorneaRoughness = 0.003;
	static const float EyeScleraRoughness = 0.025;
	static const float EyeIrisDepth = 0.15;
	static const float EyeIrisConcavity = 0.45;

	// Per-avatar controls compose with the authored material. Neutral defaults keep
	// material previews unchanged; colour alpha enables an explicit sRGB override.
	float4 g_vAvatarEyeColor < Attribute( "eye_color" ); Default4( 0, 0, 0, 0 ); >;
	float g_flAvatarEyeSize < Attribute( "eye_size" ); Default( 1 ); >;
	float2 g_vAvatarEyeAlign < Attribute( "eye_align" ); Default2( 0, 0 ); >;
	float g_flAvatarEyePupilSize < Attribute( "eye_pupil_size" ); Default( 1 ); >;

	#if ( D_OPAQUE_FADE ) && ( !S_MODE_DEPTH )
		RenderState( AlphaToCoverageEnable, true );
	#endif

	float EyeIrisRadius()
	{
		return clamp( g_flIrisRadius * g_flAvatarEyeSize, 0.02, 0.48 );
	}

	float EyePupilSize()
	{
		return clamp( g_flPupilSize * g_flAvatarEyePupilSize, 0.02, 0.95 );
	}

	float EyeDisc( float distance, float softness )
	{
		float width = max( fwidth( distance ), max( softness, 0.0001 ) );
		return 1 - smoothstep( -width, width, distance );
	}

	float EyeHash( float value )
	{
		return frac( sin( value * 127.1 + g_flDetailSeed * 311.7 ) * 43758.5453 );
	}

	// Periodic noise avoids an iris seam at atan2's wrap from pi to -pi.
	float EyeFiberNoise( float angle, float count )
	{
		count = max( floor( count ), 1 );
		float coordinate = frac( angle ) * count;
		float cell = floor( coordinate );
		float blend = frac( coordinate );
		blend = blend * blend * ( 3 - 2 * blend );
		return lerp( EyeHash( cell ), EyeHash( fmod( cell + 1, count ) ), blend );
	}

	float EyeHash2( float2 p )
	{
		float3 q = frac( float3( p.x, p.y, p.x ) * 0.1031 + g_flDetailSeed * 0.017 );
		q += dot( q, q.yzx + 33.33 );
		return frac( (q.x + q.y) * q.z );
	}

	// Wrap lattice indices, not the noise value: both sides of the polar seam agree.
	float EyePolarNoise( float2 p, float period )
	{
		float2 cell = floor( p );
		float2 blend = frac( p );
		blend = blend * blend * (3 - 2 * blend);
		float x0 = cell.x - period * floor( cell.x / period );
		float x1 = x0 + 1 - period * floor( (x0 + 1) / period );
		return lerp(
			lerp( EyeHash2( float2( x0, cell.y ) ), EyeHash2( float2( x1, cell.y ) ), blend.x ),
			lerp( EyeHash2( float2( x0, cell.y + 1 ) ), EyeHash2( float2( x1, cell.y + 1 ) ), blend.x ), blend.y );
	}

	float EyeDetailVisibility( float frequency, float footprint )
	{
		return 1 - smoothstep( 0.25, 0.9, frequency * footprint );
	}

	// Integrate a line over the pixel footprint. Subpixel lines lose intensity
	// instead of getting wider and adding more colour as the camera pulls back.
	float EyeFilteredLine( float distance, float halfWidth, float footprint )
	{
		float pixelWidth = max( footprint, 0.00001 );
		float left = max( distance - pixelWidth * 0.5, -halfWidth );
		float right = min( distance + pixelWidth * 0.5, halfWidth );
		return saturate( (right - left) / pixelWidth );
	}

	// Thin individual strands with independent width, waviness and radial length.
	// A second strand diverges from each trunk instead of tracing noise contours.
	float EyeFilaments( float angle, float radial, float count, float angularFootprint, float radialFootprint )
	{
		float coordinate = angle * count;
		float cell = floor( coordinate );
		float result = 0;
		// Include the slopes of both bend octaves and the branch divergence.
		float aa = angularFootprint * count + radialFootprint * 13;
		[unroll]
		for ( int neighbour = -1; neighbour <= 1; neighbour++ )
		{
			float index = cell + neighbour;
			float wrappedIndex = index - count * floor( index / count );
			float seed = EyeHash2( float2( wrappedIndex, 127 ) );
			float seed2 = EyeHash2( float2( wrappedIndex, 231 ) );
			// Each bundle changes direction and thickness independently. A shared
			// sinusoid makes neighbouring fibres resemble evenly combed wires.
			float growthNoise = EyePolarNoise( float2( wrappedIndex, radial * 7 + 311 ), count );
			float grain = lerp( 0.5, EyePolarNoise( float2( wrappedIndex, radial * 31 + 733 ), count ),
				EyeDetailVisibility( 31, radialFootprint ) );
			float bend = (EyePolarNoise( float2( wrappedIndex, radial * 4 + 519 ), count ) - 0.5) * 0.9
				+ (grain - 0.5) * 0.12;
			float distance = coordinate - index - (0.25 + seed * 0.5) + bend;
			float width = lerp( 0.035, 0.13, seed ) * lerp( 0.25, 1.6, growthNoise );
			float fork = smoothstep( 0.1 + seed * 0.55, 0.85, radial ) * (seed2 - 0.5) * 1.7;
			float trunk = EyeFilteredLine( distance, width, aa );
			float branch = EyeFilteredLine( distance + fork, width * 0.6, aa );

			// Integrate the union before combining: overlapping branches must not
			// double the trunk's brightness, even when both fit inside one pixel.
			float overlapLeft = max( -width, -fork - width * 0.6 );
			float overlapRight = min( width, -fork + width * 0.6 );
			float overlapWidth = max( overlapRight - overlapLeft, 0 );
			float overlap = EyeFilteredLine( distance - (overlapLeft + overlapRight) * 0.5, overlapWidth * 0.5, aa );
			float coverage = trunk + (branch - overlap) * 0.5;

			// Remove each filament's mean locally rather than a fixed brightness
			// offset, so filtering out the strands doesn't brighten the whole iris.
			float mean = 2 * width + (1.2 * width - overlapWidth) * 0.5;
			float meanWeight = saturate( 1 - abs( coordinate - index - 0.5 ) );
			float start = seed * 0.45;
			float end = 0.5 + seed2 * 0.55;
			float envelope = smoothstep( start, start + 0.12, radial )
				* (1 - smoothstep( end - 0.15, end, radial ));
			float continuity = smoothstep( 0.15, 0.6, growthNoise ) * lerp( 0.7, 1.2, grain );
			result += (coverage - mean * meanWeight) * envelope * continuity * (0.25 + seed2 * 0.75);
		}
		return result;
	}

	// Independent tapered bundles: longitudinal ridges carry the iris structure,
	// while the neighbouring clefts provide depth without outlining noise cells.
	float EyeRadialBundles( float angle, float radial, float angularFootprint, float radialFootprint )
	{
		const float count = 137;
		float coordinate = angle * count;
		float cell = floor( coordinate );
		float result = 0;
		[unroll]
		for ( int neighbour = -1; neighbour <= 1; neighbour++ )
		{
			float index = cell + neighbour;
			float wrapped = index - count * floor( index / count );
			float seed = EyeHash2( float2( wrapped, 941 ) );
			float seed2 = EyeHash2( float2( wrapped, 1031 ) );
			float bend = (EyePolarNoise( float2( wrapped, radial * 2.5 + 829 ), count ) - 0.5) * 0.65;
			float distance = coordinate - index - 0.2 - seed * 0.6 + bend;
			float width = lerp( 0.07, 0.24, seed2 ) * lerp( 0.55, 1.0, sin( saturate( radial ) * M_PI ) );
			float footprint = max( angularFootprint * count + radialFootprint * 2.5, 0.0001 );
			// Convolve rounded bundle profiles with the pixel footprint. Flat-topped
			// line coverage made the tissue resemble strips with cut edges.
			float filteredWidth = sqrt( width * width + footprint * footprint * 0.25 );
			float ridge = exp2( -2 * pow( distance / filteredWidth, 2 ) ) * width / filteredWidth;
			float cleft = exp2( -2 * pow( (distance + width * 1.8) / filteredWidth, 2 ) ) * width / filteredWidth;
			float start = seed * 0.2;
			float end = lerp( 0.45, 1.15, seed2 );
			float envelope = smoothstep( start, start + 0.09, radial )
				* (1 - smoothstep( end - 0.2, end, radial ));
			float continuityNoise = lerp( 0.5, EyePolarNoise( float2( wrapped, radial * 7 + 1137 ), count ),
				EyeDetailVisibility( 7, radialFootprint ) );
			float continuity = lerp( 0.15, 1, continuityNoise );
			// Integral of exp2(-2*x*x/width^2), less the neighbouring cleft.
			// The tent partitions the mean between cells without adding a seam.
			float mean = width * 1.505384 * (1 - 0.65);
			float meanWeight = saturate( 1 - abs( coordinate - index - 0.5 ) );
			result += (ridge - cleft * 0.65 - mean * meanWeight) * envelope * continuity * lerp( 0.4, 1, seed );
		}
		// Once several fibres fall inside a pixel, the three-cell neighbourhood
		// cannot integrate the entire footprint. Fade the zero-mean residual.
		return result * EyeDetailVisibility( count, angularFootprint ) * EyeDetailVisibility( 7, radialFootprint );
	}

	struct IrisTissue
	{
		float fibers;
		float distanceFibers;
		float microFibers;
		float crypts;
		float furrows;
		float pigment;
		float pupilRim;
		float innerZone;
		float mottling;
		float height;
	};

	IrisTissue EvaluateIrisTissue( float2 point, float footprint )
	{
		IrisTissue tissue = (IrisTissue)0;
		float radius = length( point );
		// Bend the tissue coordinates in both directions. Fade the displacement
		// at the physical pupil and limbus; their masks use the original radius.
		float pupilBoundary = EyePupilSize();
		float span = max( 1 - pupilBoundary, 0.05 );
		float tissueRadial = saturate( (radius - pupilBoundary) / span );
		float flowEnvelope = sin( tissueRadial * M_PI ) * min( 1, span / 0.7 );
		float2 flow = float2( EyePolarNoise( point * 5 + 239, 4096 ),
			EyePolarNoise( point * 5 + 719, 4096 ) ) - 0.5;
		float2 tissuePoint = point + flow * 0.06 * flowEnvelope;
		float angle = atan2( tissuePoint.y, tissuePoint.x + 0.000001 ) / M_2PI;
		// Tissue compresses between the circular pupil and limbus as the pupil dilates.
		float radial = saturate( (length( tissuePoint ) - pupilBoundary) / span );
		float angularFootprint = footprint / (M_2PI * max( radius, 0.05 ));
		float radialFootprint = max( footprint / span, fwidth( radial ) );
		const float count = EyeFiberCount;
		float collar = 0.29 + (EyeFiberNoise( angle + 0.31, 19 ) - 0.5) * 0.38;
		float collarDistance = radial - collar;
		float outerZone = smoothstep( -0.055, 0.055, collarDistance );

		// Large bundles wander and split into finer strands as they cross the iris.
		float2 direction = float2( cos( angle * M_2PI ), sin( angle * M_2PI ) );
		angularFootprint = max( angularFootprint, max( length( ddx( direction ) ), length( ddy( direction ) ) ) / M_2PI );
		// Fine fibres inherit the same broad structure that remains at distance.
		// Keep these bands present up close, filtering their residual detail away
		// independently rather than crossfading to an unrelated iris pattern.
		const float mediumCount = count / 4;
		const float broadCount = count / 16;
		float broad = EyePolarNoise( float2( angle * broadCount, radial + 113 ), broadCount );
		float medium = EyePolarNoise( float2( angle * mediumCount, radial * 2 + 67 ), mediumCount );
		// The pupillary ruff and outer bundles have independently seeded grain.
		// Blend over the uneven collarette instead of introducing a hard seam.
		float bundleDetail = lerp(
			EyePolarNoise( float2( angle * count + 23, radial * 7 + 211 ), count ),
			EyePolarNoise( float2( angle * count, radial * 4.8 ), count ), outerZone );
		float bundle = broad * 0.5 + medium * 0.3 + bundleDetail * 0.2;
		float strands = lerp(
			EyePolarNoise( float2( angle * count * 3 + 107 + bundle, radial * 12 + 391 ), count * 3 ),
			EyePolarNoise( float2( angle * count * 3 + bundle * 1.5, radial * 8 + 19 ), count * 3 ), outerZone );
		float fine = EyePolarNoise( float2( angle * count * 4 + strands, radial * 18 + 43 ), count * 4 );
		float bundleVisibility = EyeDetailVisibility( count, angularFootprint ) * EyeDetailVisibility( 7, radialFootprint );
		float strandVisibility = EyeDetailVisibility( count * 3, angularFootprint ) * EyeDetailVisibility( 12, radialFootprint );
		float fineVisibility = EyeDetailVisibility( count * 4, angularFootprint ) * EyeDetailVisibility( 18, radialFootprint );
		float mediumVisibility = EyeDetailVisibility( mediumCount, angularFootprint ) * EyeDetailVisibility( 2, radialFootprint );
		float broadVisibility = EyeDetailVisibility( broadCount, angularFootprint ) * EyeDetailVisibility( 1, radialFootprint );
		float broadFibers = (broad - 0.5) * broadVisibility;
		float mediumFibers = (medium - 0.5) * mediumVisibility;
		tissue.fibers = broadFibers * 0.65 + mediumFibers + (bundleDetail - 0.5) * bundleVisibility * 0.65;

		float threads = EyeFilaments( angle, radial, count * 1.75, angularFootprint, radialFootprint );
		tissue.microFibers = ((strands - 0.5) * 0.9 * strandVisibility + threads * 1.5 * EyeDetailVisibility( count * 1.75, angularFootprint )
			+ (fine - 0.5) * 0.9 * fineVisibility) * (0.5 + bundle);

		// Distance strength only boosts surviving parent bands; it never introduces
		// new markings or extra relief. Even the broadest band is pixel-filtered.
		tissue.distanceFibers = (broadFibers * (1 - mediumVisibility * 0.5) + mediumFibers * 0.5)
			* (1 - bundleVisibility);

		// Pigment islands cross radial bundles. Cartesian noise avoids making
		// every feature share the same polar symmetry and angular frequency.
		float2 pigmentPoint = point + float2( EyePolarNoise( point * 3 + 53, 4096 ), EyePolarNoise( point * 3 + 97, 4096 ) ) * 0.24;
		float islands = EyePolarNoise( pigmentPoint * 4.5 + 173, 4096 );
		float patches = EyePolarNoise( pigmentPoint * 11 + islands * 1.7 + 337, 4096 );
		float granules = EyePolarNoise( pigmentPoint * 31 + patches + 719, 4096 );
		tissue.mottling = (islands - 0.5) * 0.5 * EyeDetailVisibility( 5, footprint ) + (patches - 0.5) * 0.3 * EyeDetailVisibility( 11, footprint )
			+ (granules - 0.5) * 0.18 * EyeDetailVisibility( 31, footprint );
		tissue.innerZone = (1 - smoothstep( -0.035, 0.055, collarDistance ))
			* lerp( 0.35, 1.0, smoothstep( 0.2, 0.75, patches ) );
		// Pigmentation interrupts the same bundles that carry fine strands.
		// Avoid equally strong radial detail around the entire circumference.
		float structureEnvelope = lerp( 0.22, 1.0, smoothstep( 0.25, 0.75, patches ) )
			* (0.55 + 0.45 * exp2( -pow( collarDistance / 0.25, 2 ) ));
		tissue.fibers *= structureEnvelope * 0.9;
		tissue.fibers += EyeRadialBundles( angle, radial, angularFootprint, radialFootprint ) * 0.24;
		tissue.microFibers *= 0.35 + 0.65 * structureEnvelope;
		// Individually placed openings interrupt the stroma instead of tracing
		// every contour of a noise field as a bright cellular network.
		const float cryptCount = 31;
		float cryptCoordinate = angle * cryptCount;
		float cryptCell = floor( cryptCoordinate );
		float2 referencePoint = direction * (0.3 + radial * 0.7);
		float referenceFootprint = max( length( ddx( referencePoint ) ), length( ddy( referencePoint ) ) );
		float edgeVariation = Simplex2D( referencePoint * 23 + g_flDetailSeed * 7.13 + 563 )
			* EyeDetailVisibility( 46, referenceFootprint );
		float opening = 0;
		float rim = 0;
		[unroll]
		for ( int neighbour = -1; neighbour <= 1; neighbour++ )
		{
			float cell = cryptCell + neighbour;
			float wrapped = cell - cryptCount * floor( cell / cryptCount );
			float seed = EyeHash2( float2( wrapped, 563 ) );
			float seed2 = EyeHash2( float2( wrapped, 673 ) );
			float seed3 = EyeHash2( float2( wrapped, 787 ) );
			float centreAngle = cell + lerp( 0.2, 0.8, seed );
			float centreRadial = lerp( 0.12, 0.48, seed2 );
			float width = lerp( 0.10, 0.32, seed3 );
			float height = lerp( 0.035, 0.13, seed );
			float2 offset = float2( (cryptCoordinate - centreAngle) / width,
				(radial - centreRadial) / height );
			// Tilt each opening and roughen its boundary without joining its
			// neighbour into a continuous ring.
			offset.x += offset.y * (seed2 - 0.5) * 0.8;
			float boundary = length( offset ) + edgeVariation * 0.22;
			float aa = max( angularFootprint * cryptCount / width + radialFootprint / height, 0.08 );
			// Stop unresolved cavities and rims from spreading across neighbouring
			// cells as the antialiasing width grows. Broad pigment remains visible.
			float enabled = smoothstep( 0.25, 0.5, seed3 ) * EyeDetailVisibility( 1, aa );
			opening = max( opening, (1 - smoothstep( 0.65 - aa, 1.0 + aa, boundary )) * enabled );
			rim = max( rim, exp2( -pow( (boundary - 1.15) / (0.22 + aa), 2 ) ) * enabled );
		}
		tissue.crypts = opening * EyeDetailVisibility( cryptCount, angularFootprint );
		tissue.fibers *= 1 - tissue.crypts;
		tissue.microFibers *= 1 - tissue.crypts;
		float cavityRim = rim * EyeDetailVisibility( cryptCount, angularFootprint );

		// Two sparse outer folds wander independently; a periodic ring wave makes
		// the iris read as concentric engraved bands even when its strength is low.
		float foldA = radial - 0.74 - (EyeFiberNoise( angle + 0.17, 13 ) - 0.5) * 0.18;
		float foldB = radial - 0.91 - (EyeFiberNoise( angle + 0.43, 19 ) - 0.5) * 0.12;
		float foldCoverage = smoothstep( 0.48, 0.72, EyeFiberNoise( angle + 0.79, 9 ) );
		tissue.furrows = (EyeFilteredLine( foldA, 0.012, radialFootprint )
			+ EyeFilteredLine( foldB, 0.0012, radialFootprint ) * 0.6) * foldCoverage;
		tissue.pigment = (EyePolarNoise( float2( angle * 11, radial * 2 + 91 ), 11 ) - 0.5)
			* EyeDetailVisibility( 11, angularFootprint ) * EyeDetailVisibility( 2, radialFootprint );
		float rimWidth = 0.008 + span * (0.008 + 0.08 * pow( EyeFiberNoise( angle + 0.19, 47 ), 2 ));
		tissue.pupilRim = (1 - smoothstep( rimWidth, rimWidth + max( footprint, 0.035 ), radius - pupilBoundary ))
			* EyeDetailVisibility( 71, angularFootprint );
		tissue.height = tissue.fibers * EyeFiberStrength * 0.4 + tissue.microFibers * EyeMicroFiberStrength * 0.2
			- tissue.crypts * EyeCryptStrength * 0.9 + cavityRim * 0.15
			- tissue.furrows * EyeRingStrength * 0.2;
		return tissue;
	}

	// Recover the tissue's slope from screen derivatives; no extra noise evaluations.
	float2 IrisReliefSlope( float height, float2 point )
	{
		float2 dx = ddx( point );
		float2 dy = ddy( point );
		float determinant = dx.x * dy.y - dx.y * dy.x;
		float2 gradient = float2( dy.y * ddx( height ) - dx.y * ddy( height ),
			dx.x * ddy( height ) - dy.x * ddx( height ) ) * sign( determinant ) / max( abs( determinant ), 0.00000001 );
		return gradient * min( 0.012 * EyeIrisRelief, 0.5 / max( length( gradient ), 0.001 ) );
	}

	float EyeVeins( float2 uv )
	{
		float radius = length( uv );
		float angle = atan2( uv.y, uv.x + 0.000001 ) / M_2PI;
		float footprint = max( length( ddx( uv ) ), length( ddy( uv ) ) );
		float coordinate = angle * 18;
		float cell = floor( coordinate );
		float veins = 0;
		[unroll]
		for ( int neighbour = -1; neighbour <= 1; neighbour++ )
		{
			float index = cell + neighbour;
			float wrapped = index - 18 * floor( index / 18 );
			float seed = EyeHash2( float2( wrapped, 317 ) );
			float seed2 = EyeHash2( float2( wrapped, 419 ) );
			// Vessels enter from the periphery and taper toward the limbus.
			float start = EyeIrisRadius() * lerp( 1.02, 1.36, seed );
			float growth = smoothstep( start, start + 0.08, radius );
			float wander = (EyePolarNoise( float2( wrapped, radius * 16 + 71 ), 18 ) - 0.5) * 0.7
				+ (EyePolarNoise( float2( wrapped, radius * 39 + 23 ), 18 ) - 0.5) * 0.12;
			float distance = coordinate - index - 0.2 - seed * 0.6 + wander;
			float width = lerp( 0.006, 0.018, seed2 ) * growth;
			float aa = footprint * 18 / (M_2PI * max( radius, 0.05 )) + footprint * 16 * 0.7 * 1.5;
			float trunk = EyeFilteredLine( distance, width, aa );
			float fork = (1 - smoothstep( start + 0.025, start + 0.18, radius )) * (seed2 - 0.5) * 2.4;
			float branch = EyeFilteredLine( distance + fork, width * 0.45, aa + footprint * 12 );
			float twigOffset = (1 - smoothstep( start, start + 0.11, radius )) * (seed - 0.5) * 1.7;
			float twig = EyeFilteredLine( distance + fork + twigOffset, width * 0.22, aa + footprint * 20 );
			veins += max( trunk, max( branch * 0.6, twig * 0.3 ) ) * growth;
		}
		return saturate( veins ) * EyeDetailVisibility( 65, footprint );
	}

	void EyeTangentFrame( float3 interpolatedNormal, float3 meshTangentU, float3 meshTangentV,
		out float3 normal, out float3 tangentU, out float3 tangentV )
	{
		// The human mesh already models a corneal bulge. Its normals are not the
		// radial normals of a sphere; preserve that authored optical surface.
		normal = normalize( interpolatedNormal );
		tangentU = normalize( meshTangentU - normal * dot( normal, meshTangentU ) );
		float handedness = dot( cross( normal, meshTangentU ), meshTangentV ) < 0 ? -1 : 1;
		tangentV = cross( normal, tangentU ) * handedness;
	}

	float3x3 EyeProjectionRotation()
	{
		float2 centre = (g_vIrisCenter + g_vAvatarEyeAlign - 0.5) / EyeUvRadius;
		centre *= min( 1, 0.999 / max( length( centre ), 0.0001 ) );
		float forward = sqrt( saturate( 1 - dot( centre, centre ) ) );

		// Rotate both the surface position and viewing ray into the same iris frame.
		float2 tilt = centre / (1 + forward);
		return float3x3(
			float3( 1 - centre.x * tilt.x, -centre.x * tilt.y, -centre.x ),
			float3( -centre.y * tilt.x, 1 - centre.y * tilt.y, -centre.y ),
			float3( centre, forward ) );
	}

	float3 EyeProjection( float2 texcoords, float hemisphere, float3x3 rotation )
	{
		// Approximate aperture depth in UV units for the recessed iris intersection.
		// Human UVs warp around a modeled bulge: this approximation must not replace
		// the mesh normals used for the optical surface.
		// Human eyes use separate UV tiles for each eye and hemisphere.
		// Repeat the procedural projection like the original eye textures.
		float2 disk = (frac( texcoords ) - 0.5) / EyeUvRadius;
		float3 sphere = float3( disk, sqrt( saturate( 1 - dot( disk, disk ) ) ) * hemisphere );
		return mul( rotation, sphere ) * EyeUvRadius;
	}

	float3x3 EyeProjectionFrame( float3 eyeForward, float3 eyeRight, float3x3 rotation )
	{
		// Carry the bind-pose axes through skinning. Human eye UVs warp around
		// the modeled cornea, so their local mesh tangents are not fixed iris axes.
		float3 forward = normalize( eyeForward );
		float3 right = eyeRight - forward * dot( eyeRight, forward );
		right /= max( length( right ), 0.0001 );
		float3 down = -cross( forward, right );
		return mul( rotation, float3x3( right, down, forward ) );
	}

	float2 IrisParallaxPoint( float3 surface, float3 ray, float irisRadius )
	{
		float rimHeight = sqrt( max( EyeUvRadius * EyeUvRadius - irisRadius * irisRadius, 0 ) );
		float2 point = surface.xy / irisRadius;
		float2 slope = ray.xy / max( -ray.z, 0.05 );
		float curvature = EyeIrisDepth * EyeIrisConcavity;

		// Intersect the refracted ray with z = -depth + curvature * radius^2.
		// The iris is below the corneal rim, not wrapped around the eyeball surface.
		float height = max( (surface.z - rimHeight) / irisRadius + EyeIrisDepth
			- curvature * dot( point, point ), 0 );
		float a = curvature * dot( slope, slope );
		float b = 1 + 2 * curvature * dot( point, slope );
		float root = sqrt( b * b + 4 * a * height );

		// Use the stable quadratic root, including flat irises and head-on views.
		float distance = b >= 0 ? 2 * height / max( b + root, 0.0001 )
			: (root - b) / max( 2 * a, 0.0001 );
		return point + slope * distance;
	}

	float3 IrisColor( float radius, float pupilMask, IrisTissue tissue )
	{
		float3 baseIrisColor = SrgbGammaToLinear( lerp( g_vIrisColor, g_vAvatarEyeColor.rgb, saturate( g_vAvatarEyeColor.a ) ) );
		// A modest warm inner zone follows the uneven collarette. Avoid a bright
		// bullseye and preserve dark brown pigmentation without a second colour tint.
		float coolPigment = saturate( (max( baseIrisColor.g, baseIrisColor.b ) - baseIrisColor.r)
			/ max( max( baseIrisColor.g, baseIrisColor.b ) * 0.6, 0.001 ) );
		float luminance = dot( baseIrisColor, float3( 0.2126, 0.7152, 0.0722 ) );
		baseIrisColor = lerp( baseIrisColor, luminance.xxx, coolPigment * 0.3 );
		// Separate inner pigmentation from the islands that interrupt fibres.
		// Blue irises retain a weaker warm zone; brown and green show more of it.
		float bluePigment = saturate( (baseIrisColor.b - baseIrisColor.g)
			/ max( baseIrisColor.b * 0.4, 0.001 ) );
		float innerBlend = tissue.innerZone * lerp( 0.6, 0.2, bluePigment );
		float3 irisColor = baseIrisColor * lerp( float3( 0.95, 1, 1.03 ), float3( 1.6, 0.9, 0.5 ), innerBlend );
		irisColor *= lerp( 1, 0.8, smoothstep( 0.65, 1, radius ) );

		// Pigment thickness changes the colour of the underlying pale tissue.
		// Per-channel absorption couples lightness and colour: exposed fibres
		// become paler without blending toward an unrelated fixed highlight tint.
		// This is an artistic absorption model, not a spectral scattering solution.
		float3 substrate = lerp( float3( 0.48, 0.42, 0.32 ), float3( 0.30, 0.40, 0.48 ), coolPigment );
		float3 target = max( irisColor * lerp( 0.55, 0.32, coolPigment ), 0.00001 );
		// Preserve the selected colour at unit density, including light colours
		// above the usual substrate. Keep logarithms finite for black channels.
		substrate = max( substrate, target );
		float3 absorption = -log( target / substrate );
		float density = exp2( -tissue.fibers * 1.8 - tissue.microFibers * 0.6
			- tissue.distanceFibers * 0.35 - tissue.mottling - tissue.pigment * 0.35 );
		irisColor = substrate * exp( -absorption * density );
		irisColor *= 1 - tissue.crypts * 0.22;
		irisColor *= 1 - tissue.furrows * EyeRingStrength * 0.7;
		irisColor *= 1 - tissue.pupilRim * EyePupilRimStrength * 0.85;

		float limbalEdge = 1 - EyeLimbalWidth + tissue.fibers * 0.025;
		float limbalMask = smoothstep( limbalEdge - 0.06, limbalEdge + 0.06, radius );
		irisColor = lerp( irisColor, baseIrisColor * 0.12, limbalMask * EyeLimbalStrength );

		// The pupil is a black opening beneath the reflective cornea.
		irisColor *= 1 - pupilMask;
		return irisColor;
	}

	float3 ScleraColor( float2 uv )
	{
		// Vascular tint grows toward the periphery relative to the iris, so mesh
		// fit does not leave smaller human eyes with an entirely featureless sclera.
		float radius = length( uv );
		float rednessStart = EyeIrisRadius() * 1.08;
		float redness = smoothstep( rednessStart, max( 0.34, rednessStart + 0.10 ), radius ) * EyeScleraRedness;
		float3 scleraColor = lerp( SrgbGammaToLinear( EyeScleraColor ), SrgbGammaToLinear( EyeScleraEdgeColor ), redness );
		float cloud = EyePolarNoise( uv * 33 + 211, 4096 ) - 0.5;
		float capillaries = EyePolarNoise( uv * 127 + cloud + 131, 4096 ) - 0.5;
		scleraColor *= 1 + cloud * float3( 0.08, 0.12, 0.15 ) + capillaries * 0.06;
		float veins = EyeVeins( uv ) + EyeVeins( float2( uv.y, -uv.x ) * 1.28 ) * 0.45;
		scleraColor = lerp( scleraColor, SrgbGammaToLinear( EyeScleraEdgeColor ) * 0.65, veins * EyeVeinStrength );
		return scleraColor;
	}

	// Filter the wet lobe by the normal variation across a pixel. The generic
	// cube-root roughness floor turns a small smooth cornea into a matte surface.
	// Adding slope variance retains highlights while still widening subpixel lobes.
	float EyeFilteredRoughness( float roughness, float3 normal )
	{
		float3 dx = ddx( normal );
		float3 dy = ddy( normal );
		float kernelRoughnessSquared = min( 0.5 * (dot( dx, dx ) + dot( dy, dy )), 0.18 );
		return sqrt( saturate( roughness * roughness + kernelRoughnessSquared ) );
	}

	// The generic directional receiver offset spans shadow-map texels, which can
	// exceed the eye's size and expose faceted bands. The eye already supplies a
	// small smooth-surface bias; do not add that coarse offset a second time.
	void ComputeEyeDirectLighting( inout LightingTerms_t lighting, FinalCombinerInput_t f, float3 receiverNormal )
	{
		// InitLightingTerms starts diffuse at white for unlit callers. Match the
		// standard direct-light path by clearing accumulators before adding lights.
		lighting.vDiffuse = 0;
		lighting.vSpecular = 0;
		lighting.vTransmissive = 0;

		if ( !LightmappedLight::UsesLightmaps() && !ProbeLight::UsesProbes() )
			ComputeDirectionalLight( f, float3( 0, 0, 0 ), lighting.vDiffuse, lighting.vSpecular, lighting.vTransmissive );

		if ( DirectionalLightDebug > 0 && g_DirectionalLightCascadeCount > 0 )
			lighting.vDiffuse += DirectionalLightShadow::GetDebugColor( f.vPositionWs );

		ClusterRange range = Cluster::Query( ClusterItemType_Light, f.vPositionSs );
		[loop]
		for ( uint item = 0; item < range.Count; item++ )
		{
			BinnedLight light = DynamicLightConstantByIndex( Cluster::LoadItem( range, item ) );
			ComputeDirectLightingForLight( f, receiverNormal, light, lighting.vDiffuse, lighting.vSpecular, lighting.vTransmissive );
		}
	}

	// The wrap model already uses the tissue normal for probes and lightmaps.
	// Correct the remaining diffuse sources without altering corneal reflections.
	void ApplyEyeIndirectDiffuse( inout LightingTerms_t lighting, FinalCombinerInput_t f )
	{
		bool hasDiffuseOverride = false;
		float4 ddgi = 0.0;

		if ( DDGI::IsEnabled() )
		{
			ddgi = DDGI::EvaluateVolumes( f.vPositionWs, f.vSSSNormalWs,
				CalculatePositionToCameraDirWs( f.vPositionWs ) );
			if ( ddgi.a >= 1.0 )
			{
				lighting.vIndirectDiffuse = ddgi.rgb;
				hasDiffuseOverride = true;
			}
		}

		if ( !hasDiffuseOverride && !LightmappedLight::UsesLightmaps() && !ProbeLight::UsesProbes() )
		{
			float3 diffuse = 0;
			float accumulated = 0;
			ClusterRange range = Cluster::Query( ClusterItemType_EnvMap, f.vPositionSs );

			for ( uint i = 0; i < range.Count; i++ )
			{
				uint index = Cluster::LoadItem( range, i );
				float3 localPosition = mul( float4( f.vPositionWs, 1 ), EnvMapWorldToLocal( index ) ).xyz;
				float feathering = EnvMapFeathering( index );
				float3 edgeDistance = min( localPosition - EnvMapBoxMins( index ), EnvMapBoxMaxs( index ) - localPosition );
				float distance = min( edgeDistance.x, min( edgeDistance.y, edgeDistance.z ) ) + 0.5;

				if ( distance + max( feathering, 0 ) < 0 )
					continue;

				float3 localNormal = mul( float4( f.vSSSNormalWs, 0 ), EnvMapWorldToLocal( index ) ).xyz;
				diffuse = lerp( diffuse, SampleEnvironmentMapLevel( localNormal, 1, index ), 1 - accumulated );
				accumulated += RemapValClamped( distance, min( -feathering, 0 ), max( -feathering, 0 ), 0, 1 );

				if ( accumulated >= 1 )
					break;
			}

			float3 ambient = lerp( 1.0, AmbientLightColor.rgb, AmbientLightColor.a );
			lighting.vIndirectDiffuse = ddgi.rgb + (1.0 - ddgi.a) * lerp( diffuse, ambient, AmbientLightColor.a );
			hasDiffuseOverride = true;
		}

		// Preserve the shared lighting function's probe-debug display on overridden diffuse.
		if ( hasDiffuseOverride && UsesBakedLightingFromProbe && g_bShowLPVVoxels )
			lighting.vIndirectDiffuse *= GetLightProbeUVWCheckerboard( f.vPositionWs );
	}

	// Nearby visible lids occlude the wet surface. A convex eyeball lies below
	// its own tangent plane, so it cannot shadow itself in this horizon test.
	// The transverse UV footprint supplies scale without extra material settings.
	float2 EyeContactVisibility( float3 position, float3 normal, float3 forward, float2 pixel, float2 uv )
	{
		float3 dx = ddx( position );
		float3 dy = ddy( position );
		float3 transverseX = dx - forward * dot( dx, forward );
		float3 transverseY = dy - forward * dot( dy, forward );
		float2 ux = ddx( uv );
		float2 uy = ddy( uv );
		float radius = EyeUvRadius * sqrt( (dot( transverseX, transverseX ) + dot( transverseY, transverseY ))
			/ max( dot( ux, ux ) + dot( uy, uy ), 0.00000001 ) );
		float range = max( radius * 1.6, 0.0001 );
		float worldPerPixel = max( length( dx ), length( dy ) );
		float pixelRadius = min( range / max( worldPerPixel, 0.0001 ), 320 );
		float horizonEnergy = 0;
		// Distribute radial samples between pixels instead of repeating four
		// distances everywhere. Fixed distances create bands parallel to the lids.
		float jitter = frac( 52.9829189 * frac( dot( floor( pixel ), float2( 0.06711056, 0.00583715 ) ) ) );

		[unroll]
		for ( int direction = 0; direction < 8; direction++ )
		{
			float angle = (direction + 0.5) * M_2PI / 8;
			float2 axis = float2( cos( angle ), sin( angle ) );
			float horizon = 0;
			[unroll]
			for ( int sampleIndex = 1; sampleIndex <= 4; sampleIndex++ )
			{
				float fraction = (sampleIndex - frac( jitter + direction * 0.618033989 )) / 4.0;
				float2 samplePixel = floor( pixel + axis * max( 1, pixelRadius * fraction * fraction ) ) + 0.5;
				float3 delta = Depth::GetWorldPosition( samplePixel ) - position;
				float distance = length( delta );
				float elevation = saturate( (dot( normal, delta ) - radius * 0.02) / max( distance, 0.001 ) );
				float falloff = 1 - smoothstep( range * 0.35, range, distance );
				float valid = all( samplePixel > 0 ) && all( samplePixel < g_vViewportSize );
				horizon = max( horizon, elevation * falloff * valid );
			}
			horizonEnergy += horizon * horizon;
		}

		// Average the four sample patterns in the pixel quad without additional
		// depth reads or a dependency on temporal antialiasing. This function stays
		// outside per-pixel branches so all four derivative lanes are available.
		float2 parity = fmod( floor( pixel ), 2 );
		horizonEnergy += ddx_fine( horizonEnergy ) * (0.5 - parity.x);
		horizonEnergy += ddy_fine( horizonEnergy ) * (0.5 - parity.y);
		// Emphasize a nearby lid even when the opposite directions are open.
		// This calibrated contact response is not a hemispherical visibility integral.
		float effectiveHorizon = sqrt( saturate( horizonEnergy / 8 ) );
		// Broad diffuse illumination is more occluded than the narrow wet reflection.
		return max( float2( 0.03, 0.12 ), pow( 1 - effectiveHorizon, float2( 3.5, 1.5 ) ) );
	}

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o = ( PS_OUTPUT )0;
		#if ( S_MODE_DEPTH && D_OPAQUE_FADE )
			OpaqueFadeDepth( i.vVertexColor.a, i.vPositionSs.xy );
		#endif

		float3 position = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 normal, tangentU, tangentV;
		EyeTangentFrame( i.vNormalWs.xyz, i.vTangentUWs.xyz, i.vTangentVWs.xyz, normal, tangentU, tangentV );
		float3 cameraToPosition = CalculateCameraToPositionDirWs( position );
		float hemisphere = dot( i.vNormalWs.xyz, i.vEyeForwardWs ) < 0 ? -1 : 1;
		float3x3 projectionRotation = EyeProjectionRotation();
		float3x3 projectionFrame = EyeProjectionFrame( i.vEyeForwardWs, i.vEyeRightWs, projectionRotation );
		float3 projection = EyeProjection( i.vTextureCoords.xy, hemisphere, projectionRotation );
		float2 uv = projection.xy;
		float irisRadius = EyeIrisRadius();
		float2 surfacePoint = uv / irisRadius;
		float surfaceRadius = length( surfacePoint );
		// Keep the opening fixed as the recessed pattern moves beneath it.
		// Sampling past the tissue edge uses the dark limbal colour, not sclera.
		float irisMask = EyeDisc( surfaceRadius - 1, EyeEdgeSoftness );
		// An orthographic projection also has a matching circle on the back of the eye.
		float projectionAA = max( fwidth( projection.z ), 0.0001 );
		float frontHemisphere = smoothstep( -projectionAA, projectionAA, projection.z );
		irisMask *= frontHemisphere;

		// Keep the added corneal curvature across the full iris. Taper only
		// outside its aperture: a stronger bump fading inside the iris can
		// reverse the optical normal gradient and duplicate a reflection.
		float cornealSlope = 0.3 * (1 - smoothstep( 1.0, 1.6, surfaceRadius ));
		float3 corneaNormal = normalize( normal + mul( float3( surfacePoint * cornealSlope, 0 ), projectionFrame ) );
		float roughness = EyeFilteredRoughness( lerp( EyeScleraRoughness, EyeCorneaRoughness, irisMask ), corneaNormal );

		// Depth is also the renderer's normal/roughness prepass. It must use
		// the same optical surface as Forward so AO and SSR see the wet eye.
		// Exit before tissue noise, scene-depth reads or lighting are evaluated.
		if ( DepthNormals::WantsDepthNormals() )
		{
			float opacity = 1;
			#if ( D_OPAQUE_FADE )
				opacity = OpaqueFade( i.vVertexColor.a, i.vPositionSs.xyzw );
			#endif
			o.vColor = DepthNormals::Output( corneaNormal, roughness, opacity );
			return o;
		}

		// Reflection and refraction share this same smooth optical surface.
		float3 refracted = refract( cameraToPosition, corneaNormal, 1 / EyeCorneaIor );
		float2 irisPoint = IrisParallaxPoint( projection, mul( projectionFrame, refracted ), irisRadius );
		float radius = length( irisPoint );
		// Keep the pupil crisp, with only pixel-footprint antialiasing.
		float pupilMask = EyeDisc( radius - EyePupilSize(), 0 );

		float footprint = max( length( ddx( irisPoint ) ), length( ddy( irisPoint ) ) );
		IrisTissue tissue = EvaluateIrisTissue( irisPoint, footprint );
		float3 irisColor = IrisColor( radius, pupilMask, tissue );
		float3 scleraColor = ScleraColor( uv );
		float3 albedo = lerp( scleraColor, irisColor, irisMask ) * i.vVertexColor.rgb;

		// Shade the same bowl that the viewing ray intersects.
		float2 irisSlope = -2 * EyeIrisDepth * EyeIrisConcavity * irisPoint;
		irisSlope += IrisReliefSlope( tissue.height, irisPoint ) * irisMask * (1 - pupilMask);
		float3 irisNormal = normalize( mul( float3( irisSlope, 1 ), projectionFrame ) );
		float diffuseWrap = EyeScleraDiffuseWrap * (1 - irisMask);

		// The shared wrap model supports separate diffuse and specular normals.
		// Evaluate lighting once: iris relief shades the tissue, while the cornea
		// stays smooth for reflections. Indirect diffuse also follows the tissue.
		FinalCombinerInput_t f = PS_InitFinalCombiner();
		// A small receiver bias prevents the faceted shadow mesh from shadowing
		// the smooth optical surface. This is local to the eye material.
		f.vPositionWs = position + normal * 0.03;
		f.vPositionWithOffsetWs = i.vPositionWithOffsetWs.xyz + normal * 0.03;
		f.vPositionSs = i.vPositionSs;
		f.vNormalWs = corneaNormal;
		f.vSSSNormalWs = normalize( lerp( normal, irisNormal, irisMask ) );
		f.vNormalTs = Vec3WsToTs( f.vSSSNormalWs, normal, tangentU, tangentV );
		f.vTangentUWs = tangentU;
		f.vTangentVWs = tangentV;
		f.vRoughness = roughness.xx;
		f.vAlbedo = albedo;
		f.vDiffuseColor = albedo;
		f.vTextureCoords = i.vTextureCoords.xy;
		f.vLightmapUV = i.vLightmapUV.xy;
		f.vSpecularColor = EyeCorneaReflectance.xxx;
		float diffuseExponent = lerp( 1.5, 1, irisMask );
		f.vSSSWrapParameters = float4( diffuseWrap, diffuseExponent, 1 / (1 + diffuseWrap),
			(1 + diffuseExponent) / (2 + 2 * diffuseWrap) );

		// Keep lighting calls outside per-pixel branches: shadow receivers use derivatives.
		LightingTerms_t lighting = InitLightingTerms();
		ComputeEyeDirectLighting( lighting, f, normal );
		// Use the tissue normal for blended DDGI, including partially covered lightmaps/probes.
		// Corneal reflections are evaluated separately below.
		FinalCombinerInput_t diffuseInput = f;
		diffuseInput.vNormalWs = f.vSSSNormalWs;
		CalculateIndirectLighting( lighting, diffuseInput );
		ApplyEyeIndirectDiffuse( lighting, f );
		// A wet dielectric reflects the environment's radiance. Rescaling its
		// reflection to the diffuse probe brightness erases the sky in sockets.
		float nDotV = saturate( dot( corneaNormal, -cameraToPosition ) );
		float3 reflectionFactor = CalcBRDFReflectionFactor( nDotV, f.vRoughness.x, f.vSpecularColor );
		lighting.vIndirectSpecular = EnvMap::From( position, i.vPositionSs, corneaNormal, f.vRoughness ) * reflectionFactor;
		// Preserve the renderer's dynamic reflection source and its confidence
		// when replacing the probe-normalized cubemap contribution.
		if ( DynamicReflections::IsEnabled() )
		{
			float4 reflection = DynamicReflections::Sample( i.vPositionSs.xy, sqrt( f.vRoughness.x ) );
			lighting.vIndirectSpecular = lerp( lighting.vIndirectSpecular, reflection.rgb * reflectionFactor, reflection.a );
		}

		// Real scene occlusion shades the socket without darkening toward the camera rim.
		float3 diffuseAO = CalculateDiffuseAmbientOcclusion( f, lighting );
		float3 specularAO = CalculateSpecularAmbientOcclusion( f, lighting );
		float2 contactVisibility = EyeContactVisibility( position, normal, normalize( i.vEyeForwardWs ), i.vPositionSs.xy, i.vTextureCoords.xy );
		// A local approximation to the warm light surviving the thin lid margin.
		// Keep the iris occlusion neutral and the exposed sclera unchanged.
		float3 contactColor = pow( contactVisibility.xxx, lerp( float3( 0.85, 1.0, 1.07 ), 1.0.xxx, irisMask ) );
		// Direct reflections already include visibility from their light's shadow.
		// Ambient socket occlusion belongs to the environment reflection only.
		o.vColor = float4( albedo * contactColor * (lighting.vDiffuse
			+ lighting.vIndirectDiffuse * diffuseAO)
			+ lighting.vSpecular + lighting.vIndirectSpecular * specularAO * contactVisibility.y, 1 );

		#if ( D_OPAQUE_FADE )
			o.vColor.a = OpaqueFade( i.vVertexColor.a, i.vPositionSs.xyzw );
		#endif

		f.flOpacity = o.vColor.a;
		return PS_FinalCombinerDoPostProcessing( f, lighting, o );
	}
}
