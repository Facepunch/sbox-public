// citizen_eye.shader written as a surface: a procedural eye with no textures. The iris is projected from the eye's own
// axes (the bind-pose +X and +Y, carried through skinning by the tangent frame), seen through a refracting cornea down to a
// recessed bowl, its tissue (fibres, crypts, furrows, pigment) filtered by the pixel's footprint; a lightly veined sclera
// around it. One layer lights it: the wet cornea's smooth normal for specular light, the tissue's normal for diffuse light
// with the sclera's wrap, roughness filtered here rather than by the engine. Ambient occlusion and a horizon test against
// the depth prepass (the lids' contact shadow) darken it. It lights itself through the engine's lighting
// (IInterpolatedLayeredSurface) with citizen_eye.shader's lighting features ([Combiner]), as citizen_eye.shader does.
import sbox.surface;

// What the material compiler makes of a .vmat's images (SlangMaterialCompiler): citizen_eye.shader's texture inputs,
// the textures made from them and its attributes, read with the material's features
#if MATERIAL_INPUTS
// The material editor's FEATURES lines, as the .shader declared them
// End of the material editor's lines
#define S_SUBSURFACE_SCATTERING SUBSURFACE_SCATTERING_WRAP
#define S_SPECULAR 1
#define S_SPECULAR_CUBE_MAP 1
#include "vr_common_ps_code.fxc"
// citizen_eye.shader's vertex inputs (its VS_INPUT, every dynamic combo's): what the model compiler lays out the meshes of
// models using these materials by (the material's VSInputSignature), so they keep the streams they had
#define VS_INPUT_HAS_TANGENT_BASIS 1
struct VS_INPUT
{
	#include "vr_shared_standard_vs_input.fxc"
};
#endif

static const float EyePi = 3.14159265358979323846;
static const float EyeTwoPi = 6.28318530717958647692;

// The cornea has one optical model: its normal-incidence reflectance from the same refractive index the viewing ray uses
static const float EyeCorneaIor = 1.376;
static const float EyeCorneaFresnelRatio = ( EyeCorneaIor - 1 ) / ( EyeCorneaIor + 1 );
static const float EyeCorneaReflectance = EyeCorneaFresnelRatio * EyeCorneaFresnelRatio;

// Shared uv scale for projection, refraction and contact occlusion
static const float EyeUvRadius = 0.49101;

// Human iris anatomy, in iris-radius units
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

// A lightly pigmented sclera under the same wet tear film as the cornea
static const float3 EyeScleraColor = float3( 0.69, 0.685, 0.67 );
static const float3 EyeScleraEdgeColor = float3( 0.58, 0.37, 0.34 );
static const float EyeScleraRedness = 0.45;
static const float EyeScleraDiffuseWrap = 0.3;
static const float EyeVeinStrength = 0.4;
static const float EyeCorneaRoughness = 0.003;
static const float EyeScleraRoughness = 0.025;
static const float EyeIrisDepth = 0.15;
static const float EyeIrisConcavity = 0.45;

// Per-avatar controls from the object's attributes, composed with the material; neutral by default
float4 g_vAvatarEyeColor < Attribute( "eye_color" ); Default4( 0, 0, 0, 0 ); >;
float g_flAvatarEyeSize < Attribute( "eye_size" ); Default( 1 ); >;
float2 g_vAvatarEyeAlign < Attribute( "eye_align" ); Default2( 0, 0 ); >;
float g_flAvatarEyePupilSize < Attribute( "eye_pupil_size" ); Default( 1 ); >;

/// The eye's axes in world space, worked out per vertex. No mesh has binormals (tangents carry their sign), so these read
/// nothing and Displace fills them.
public struct CitizenEyeVaryings
{
	public float3 Forward : BINORMAL0;   // the bind-pose +X
	public float3 Right : BINORMAL1;     // the bind-pose +Y
}

/// What the iris's tissue adds up to at a point.
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
}

float Square( float x ) { return x * x; }

// 1 / 289 whatever the modulus, as procedural.hlsl's SIMPLEX_MOD
float2 SimplexMod( float2 x, float m ) { return x - floor( x * 0.0034602076124567 ) * m; }
float3 SimplexMod( float3 x, float m ) { return x - floor( x * 0.0034602076124567 ) * m; }
float3 SimplexPermute( float3 x ) { return SimplexMod( x * x * 34.0 + x, 289.0 ); }

/// procedural.hlsl's Simplex2D.
float Simplex2D( float2 uv )
{
	const float4 constants = float4( 0.2113248654, 0.36602540378, -0.5773502692, 0.0243902439 );
	float2 corner = floor( uv + dot( uv, constants.yy ) );
	float2 x0 = uv - corner + dot( corner, constants.xx );
	float2 i1 = ( x0.x > x0.y ) ? float2( 1.0, 0.0 ) : float2( 0.0, 1.0 );
	float4 x12 = x0.xyxy + constants.xxzz;
	x12.xy -= i1;
	corner = SimplexMod( corner, 289.0 );
	float3 permutation = SimplexPermute( SimplexPermute( corner.y + float3( 0.0, i1.y, 1.0 ) ) + corner.x + float3( 0.0, i1.x, 1.0 ) );
	float3 contribution = max( 0.5 - float3( dot( x0, x0 ), dot( x12.xy, x12.xy ), dot( x12.zw, x12.zw ) ), 0.0 );
	contribution = contribution * contribution;
	contribution = contribution * contribution;
	float3 x = 2.0 * frac( permutation * constants.www ) - 1.0;
	float3 h = abs( x ) - 0.5;
	float3 ox = floor( x + 0.5 );
	float3 a0 = x - ox;
	contribution *= rsqrt( a0 * a0 + h * h );
	float3 gradient = float3( a0.x * x0.x + h.x * x0.y, a0.yz * x12.xz + h.yz * x12.yw );
	return 130.0 * dot( contribution, gradient );
}

float EyeDetailVisibility( float frequency, float footprint )
{
	return 1 - smoothstep( 0.25, 0.9, frequency * footprint );
}

// A line integrated over the pixel's footprint: subpixel lines lose intensity instead of widening as the camera pulls back
float EyeFilteredLine( float distance, float halfWidth, float footprint )
{
	float pixelWidth = max( footprint, 0.00001 );
	float left = max( distance - pixelWidth * 0.5, -halfWidth );
	float right = min( distance + pixelWidth * 0.5, halfWidth );
	return saturate( ( right - left ) / pixelWidth );
}

float EyeDisc( float distance, float softness )
{
	float width = max( fwidth( distance ), max( softness, 0.0001 ) );
	return 1 - smoothstep( -width, width, distance );
}

[Material, Combiner( "S_SUBSURFACE_SCATTERING = SUBSURFACE_SCATTERING_WRAP, S_SPECULAR = 1" )]
public struct CitizenEye : IInterpolatedLayeredSurface, IInterpolatedVertexSurface
{
	typealias Varyings = CitizenEyeVaryings;

	// Identity and mesh fit are the only artist controls
	[Group( "Iris" ), Default3( 0.28, 0.19, 0.11 )] float3 IrisColor;
	[Group( "Iris" ), Default( 1 ), Range( 0, 100 )] float DetailSeed;
	[Group( "Iris" ), Default( 0.30 ), Range( 0.15, 0.75 )] float PupilSize;
	[Group( "Mesh Fit" ), Default( 0.20 ), Range( 0.10, 0.30 )] float IrisRadius;
	[Group( "Mesh Fit" ), Default2( 0.5, 0.5 ), Range( 0.35, 0.65 )] float2 IrisCenter;

	public CitizenEyeVaryings Vertex( CitizenEyeVaryings v )
	{
		return v;
	}

	/// The bind-pose axes through the vertex's tangent frame: they follow skinning and gaze without bone indices.
	public CitizenEyeVaryings Displace( inout WorldVertexInput v, CitizenEyeVaryings values )
	{
		float3 normal = v.ObjectNormal;
		float4 tangent = v.ObjectTangent;
		float3 bitangent = cross( normal, tangent.xyz ) * tangent.w;
		CitizenEyeVaryings o;
		o.Forward = v.TangentU * tangent.x + v.TangentV * bitangent.x + v.Normal * normal.x;
		o.Right = v.TangentU * tangent.y + v.TangentV * bitangent.y + v.Normal * normal.y;
		return o;
	}

	float EyeIrisRadius()
	{
		return clamp( IrisRadius * g_flAvatarEyeSize, 0.02, 0.48 );
	}

	float EyePupilSize()
	{
		return clamp( PupilSize * g_flAvatarEyePupilSize, 0.02, 0.95 );
	}

	float EyeHash( float value )
	{
		return frac( sin( value * 127.1 + DetailSeed * 311.7 ) * 43758.5453 );
	}

	// Periodic noise: no iris seam at atan2's wrap from pi to -pi
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
		float3 q = frac( float3( p.x, p.y, p.x ) * 0.1031 + DetailSeed * 0.017 );
		q += dot( q, q.yzx + 33.33 );
		return frac( ( q.x + q.y ) * q.z );
	}

	// The lattice indices wrap, not the noise value: both sides of the polar seam agree
	float EyePolarNoise( float2 p, float period )
	{
		float2 cell = floor( p );
		float2 blend = frac( p );
		blend = blend * blend * ( 3 - 2 * blend );
		float x0 = cell.x - period * floor( cell.x / period );
		float x1 = x0 + 1 - period * floor( ( x0 + 1 ) / period );
		return lerp(
			lerp( EyeHash2( float2( x0, cell.y ) ), EyeHash2( float2( x1, cell.y ) ), blend.x ),
			lerp( EyeHash2( float2( x0, cell.y + 1 ) ), EyeHash2( float2( x1, cell.y + 1 ) ), blend.x ), blend.y );
	}

	// Thin strands with their own width, waviness and radial length; a second strand diverges from each trunk
	float EyeFilaments( float angle, float radial, float count, float angularFootprint, float radialFootprint )
	{
		float coordinate = angle * count;
		float cell = floor( coordinate );
		float result = 0;
		float aa = angularFootprint * count + radialFootprint * 13;
		[unroll]
		for ( int neighbour = -1; neighbour <= 1; neighbour++ )
		{
			float index = cell + neighbour;
			float wrappedIndex = index - count * floor( index / count );
			float seed = EyeHash2( float2( wrappedIndex, 127 ) );
			float seed2 = EyeHash2( float2( wrappedIndex, 231 ) );
			float growthNoise = EyePolarNoise( float2( wrappedIndex, radial * 7 + 311 ), count );
			float grain = lerp( 0.5, EyePolarNoise( float2( wrappedIndex, radial * 31 + 733 ), count ), EyeDetailVisibility( 31, radialFootprint ) );
			float bend = ( EyePolarNoise( float2( wrappedIndex, radial * 4 + 519 ), count ) - 0.5 ) * 0.9 + ( grain - 0.5 ) * 0.12;
			float distance = coordinate - index - ( 0.25 + seed * 0.5 ) + bend;
			float width = lerp( 0.035, 0.13, seed ) * lerp( 0.25, 1.6, growthNoise );
			float fork = smoothstep( 0.1 + seed * 0.55, 0.85, radial ) * ( seed2 - 0.5 ) * 1.7;
			float trunk = EyeFilteredLine( distance, width, aa );
			float branch = EyeFilteredLine( distance + fork, width * 0.6, aa );

			// The union integrated before combining: overlapping branches don't double the trunk's brightness
			float overlapLeft = max( -width, -fork - width * 0.6 );
			float overlapRight = min( width, -fork + width * 0.6 );
			float overlapWidth = max( overlapRight - overlapLeft, 0 );
			float overlap = EyeFilteredLine( distance - ( overlapLeft + overlapRight ) * 0.5, overlapWidth * 0.5, aa );
			float coverage = trunk + ( branch - overlap ) * 0.5;

			// Each filament's mean removed locally, so filtering the strands out doesn't brighten the iris
			float mean = 2 * width + ( 1.2 * width - overlapWidth ) * 0.5;
			float meanWeight = saturate( 1 - abs( coordinate - index - 0.5 ) );
			float start = seed * 0.45;
			float end = 0.5 + seed2 * 0.55;
			float envelope = smoothstep( start, start + 0.12, radial ) * ( 1 - smoothstep( end - 0.15, end, radial ) );
			float continuity = smoothstep( 0.15, 0.6, growthNoise ) * lerp( 0.7, 1.2, grain );
			result += ( coverage - mean * meanWeight ) * envelope * continuity * ( 0.25 + seed2 * 0.75 );
		}
		return result;
	}

	// Tapered bundles: ridges carry the structure, the clefts beside them its depth
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
			float bend = ( EyePolarNoise( float2( wrapped, radial * 2.5 + 829 ), count ) - 0.5 ) * 0.65;
			float distance = coordinate - index - 0.2 - seed * 0.6 + bend;
			float width = lerp( 0.07, 0.24, seed2 ) * lerp( 0.55, 1.0, sin( saturate( radial ) * EyePi ) );
			float footprint = max( angularFootprint * count + radialFootprint * 2.5, 0.0001 );
			// Rounded bundle profiles convolved with the pixel's footprint
			float filteredWidth = sqrt( width * width + footprint * footprint * 0.25 );
			float ridge = exp2( -2 * Square( distance / filteredWidth ) ) * width / filteredWidth;
			float cleft = exp2( -2 * Square( ( distance + width * 1.8 ) / filteredWidth ) ) * width / filteredWidth;
			float start = seed * 0.2;
			float end = lerp( 0.45, 1.15, seed2 );
			float envelope = smoothstep( start, start + 0.09, radial ) * ( 1 - smoothstep( end - 0.2, end, radial ) );
			float continuityNoise = lerp( 0.5, EyePolarNoise( float2( wrapped, radial * 7 + 1137 ), count ), EyeDetailVisibility( 7, radialFootprint ) );
			float continuity = lerp( 0.15, 1, continuityNoise );
			// The integral of exp2(-2*x*x/width^2), less the neighbouring cleft, shared between cells by a tent
			float mean = width * 1.505384 * ( 1 - 0.65 );
			float meanWeight = saturate( 1 - abs( coordinate - index - 0.5 ) );
			result += ( ridge - cleft * 0.65 - mean * meanWeight ) * envelope * continuity * lerp( 0.4, 1, seed );
		}
		// The zero-mean residual faded once several fibres fall inside a pixel
		return result * EyeDetailVisibility( count, angularFootprint ) * EyeDetailVisibility( 7, radialFootprint );
	}

	IrisTissue EvaluateIrisTissue( float2 point, float footprint )
	{
		IrisTissue tissue = {};
		float radius = length( point );
		// The tissue's coordinates bent both ways, the bend faded at the pupil and limbus
		float pupilBoundary = EyePupilSize();
		float span = max( 1 - pupilBoundary, 0.05 );
		float tissueRadial = saturate( ( radius - pupilBoundary ) / span );
		float flowEnvelope = sin( tissueRadial * EyePi ) * min( 1, span / 0.7 );
		float2 flow = float2( EyePolarNoise( point * 5 + 239, 4096 ), EyePolarNoise( point * 5 + 719, 4096 ) ) - 0.5;
		float2 tissuePoint = point + flow * 0.06 * flowEnvelope;
		float angle = atan2( tissuePoint.y, tissuePoint.x + 0.000001 ) / EyeTwoPi;
		// Tissue compresses between the pupil and the limbus as the pupil dilates
		float radial = saturate( ( length( tissuePoint ) - pupilBoundary ) / span );
		float angularFootprint = footprint / ( EyeTwoPi * max( radius, 0.05 ) );
		float radialFootprint = max( footprint / span, fwidth( radial ) );
		const float count = EyeFiberCount;
		float collar = 0.29 + ( EyeFiberNoise( angle + 0.31, 19 ) - 0.5 ) * 0.38;
		float collarDistance = radial - collar;
		float outerZone = smoothstep( -0.055, 0.055, collarDistance );

		// Large bundles wander and split into finer strands across the iris
		float2 direction = float2( cos( angle * EyeTwoPi ), sin( angle * EyeTwoPi ) );
		angularFootprint = max( angularFootprint, max( length( ddx( direction ) ), length( ddy( direction ) ) ) / EyeTwoPi );
		const float mediumCount = count / 4;
		const float broadCount = count / 16;
		float broad = EyePolarNoise( float2( angle * broadCount, radial + 113 ), broadCount );
		float medium = EyePolarNoise( float2( angle * mediumCount, radial * 2 + 67 ), mediumCount );
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
		float broadFibers = ( broad - 0.5 ) * broadVisibility;
		float mediumFibers = ( medium - 0.5 ) * mediumVisibility;
		tissue.fibers = broadFibers * 0.65 + mediumFibers + ( bundleDetail - 0.5 ) * bundleVisibility * 0.65;

		float threads = EyeFilaments( angle, radial, count * 1.75, angularFootprint, radialFootprint );
		tissue.microFibers = ( ( strands - 0.5 ) * 0.9 * strandVisibility + threads * 1.5 * EyeDetailVisibility( count * 1.75, angularFootprint )
			+ ( fine - 0.5 ) * 0.9 * fineVisibility ) * ( 0.5 + bundle );

		// Distance strength only boosts surviving parent bands
		tissue.distanceFibers = ( broadFibers * ( 1 - mediumVisibility * 0.5 ) + mediumFibers * 0.5 ) * ( 1 - bundleVisibility );

		// Pigment islands across the radial bundles, from cartesian noise
		float2 pigmentPoint = point + float2( EyePolarNoise( point * 3 + 53, 4096 ), EyePolarNoise( point * 3 + 97, 4096 ) ) * 0.24;
		float islands = EyePolarNoise( pigmentPoint * 4.5 + 173, 4096 );
		float patches = EyePolarNoise( pigmentPoint * 11 + islands * 1.7 + 337, 4096 );
		float granules = EyePolarNoise( pigmentPoint * 31 + patches + 719, 4096 );
		tissue.mottling = ( islands - 0.5 ) * 0.5 * EyeDetailVisibility( 5, footprint ) + ( patches - 0.5 ) * 0.3 * EyeDetailVisibility( 11, footprint )
			+ ( granules - 0.5 ) * 0.18 * EyeDetailVisibility( 31, footprint );
		tissue.innerZone = ( 1 - smoothstep( -0.035, 0.055, collarDistance ) ) * lerp( 0.35, 1.0, smoothstep( 0.2, 0.75, patches ) );
		// Pigmentation interrupts the bundles that carry fine strands
		float structureEnvelope = lerp( 0.22, 1.0, smoothstep( 0.25, 0.75, patches ) ) * ( 0.55 + 0.45 * exp2( -Square( collarDistance / 0.25 ) ) );
		tissue.fibers *= structureEnvelope * 0.9;
		tissue.fibers += EyeRadialBundles( angle, radial, angularFootprint, radialFootprint ) * 0.24;
		tissue.microFibers *= 0.35 + 0.65 * structureEnvelope;

		// Individually placed openings interrupt the stroma
		const float cryptCount = 31;
		float cryptCoordinate = angle * cryptCount;
		float cryptCell = floor( cryptCoordinate );
		float2 referencePoint = direction * ( 0.3 + radial * 0.7 );
		float referenceFootprint = max( length( ddx( referencePoint ) ), length( ddy( referencePoint ) ) );
		float edgeVariation = Simplex2D( referencePoint * 23 + DetailSeed * 7.13 + 563 ) * EyeDetailVisibility( 46, referenceFootprint );
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
			float2 offset = float2( ( cryptCoordinate - centreAngle ) / width, ( radial - centreRadial ) / height );
			offset.x += offset.y * ( seed2 - 0.5 ) * 0.8;
			float boundary = length( offset ) + edgeVariation * 0.22;
			float aa = max( angularFootprint * cryptCount / width + radialFootprint / height, 0.08 );
			float enabled = smoothstep( 0.25, 0.5, seed3 ) * EyeDetailVisibility( 1, aa );
			opening = max( opening, ( 1 - smoothstep( 0.65 - aa, 1.0 + aa, boundary ) ) * enabled );
			rim = max( rim, exp2( -Square( ( boundary - 1.15 ) / ( 0.22 + aa ) ) ) * enabled );
		}
		tissue.crypts = opening * EyeDetailVisibility( cryptCount, angularFootprint );
		tissue.fibers *= 1 - tissue.crypts;
		tissue.microFibers *= 1 - tissue.crypts;
		float cavityRim = rim * EyeDetailVisibility( cryptCount, angularFootprint );

		// Two sparse outer folds
		float foldA = radial - 0.74 - ( EyeFiberNoise( angle + 0.17, 13 ) - 0.5 ) * 0.18;
		float foldB = radial - 0.91 - ( EyeFiberNoise( angle + 0.43, 19 ) - 0.5 ) * 0.12;
		float foldCoverage = smoothstep( 0.48, 0.72, EyeFiberNoise( angle + 0.79, 9 ) );
		tissue.furrows = ( EyeFilteredLine( foldA, 0.012, radialFootprint ) + EyeFilteredLine( foldB, 0.0012, radialFootprint ) * 0.6 ) * foldCoverage;
		tissue.pigment = ( EyePolarNoise( float2( angle * 11, radial * 2 + 91 ), 11 ) - 0.5 )
			* EyeDetailVisibility( 11, angularFootprint ) * EyeDetailVisibility( 2, radialFootprint );
		float rimWidth = 0.008 + span * ( 0.008 + 0.08 * Square( EyeFiberNoise( angle + 0.19, 47 ) ) );
		tissue.pupilRim = ( 1 - smoothstep( rimWidth, rimWidth + max( footprint, 0.035 ), radius - pupilBoundary ) ) * EyeDetailVisibility( 71, angularFootprint );
		tissue.height = tissue.fibers * EyeFiberStrength * 0.4 + tissue.microFibers * EyeMicroFiberStrength * 0.2
			- tissue.crypts * EyeCryptStrength * 0.9 + cavityRim * 0.15
			- tissue.furrows * EyeRingStrength * 0.2;
		return tissue;
	}

	// The tissue's slope from screen derivatives, with no more noise evaluations
	static float2 IrisReliefSlope( float height, float2 point )
	{
		float2 dx = ddx( point );
		float2 dy = ddy( point );
		float determinant = dx.x * dy.y - dx.y * dy.x;
		float2 gradient = float2( dy.y * ddx( height ) - dx.y * ddy( height ), dx.x * ddy( height ) - dy.x * ddx( height ) )
			* sign( determinant ) / max( abs( determinant ), 0.00000001 );
		return gradient * min( 0.012 * EyeIrisRelief, 0.5 / max( length( gradient ), 0.001 ) );
	}

	float EyeVeins( float2 uv )
	{
		float radius = length( uv );
		float angle = atan2( uv.y, uv.x + 0.000001 ) / EyeTwoPi;
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
			// Vessels enter from the periphery and taper toward the limbus
			float start = EyeIrisRadius() * lerp( 1.02, 1.36, seed );
			float growth = smoothstep( start, start + 0.08, radius );
			float wander = ( EyePolarNoise( float2( wrapped, radius * 16 + 71 ), 18 ) - 0.5 ) * 0.7
				+ ( EyePolarNoise( float2( wrapped, radius * 39 + 23 ), 18 ) - 0.5 ) * 0.12;
			float distance = coordinate - index - 0.2 - seed * 0.6 + wander;
			float width = lerp( 0.006, 0.018, seed2 ) * growth;
			float aa = footprint * 18 / ( EyeTwoPi * max( radius, 0.05 ) ) + footprint * 16 * 0.7 * 1.5;
			float trunk = EyeFilteredLine( distance, width, aa );
			float fork = ( 1 - smoothstep( start + 0.025, start + 0.18, radius ) ) * ( seed2 - 0.5 ) * 2.4;
			float branch = EyeFilteredLine( distance + fork, width * 0.45, aa + footprint * 12 );
			float twigOffset = ( 1 - smoothstep( start, start + 0.11, radius ) ) * ( seed - 0.5 ) * 1.7;
			float twig = EyeFilteredLine( distance + fork + twigOffset, width * 0.22, aa + footprint * 20 );
			veins += max( trunk, max( branch * 0.6, twig * 0.3 ) ) * growth;
		}
		return saturate( veins ) * EyeDetailVisibility( 65, footprint );
	}

	float3x3 EyeProjectionRotation()
	{
		float2 centre = ( IrisCenter + g_vAvatarEyeAlign - 0.5 ) / EyeUvRadius;
		centre *= min( 1, 0.999 / max( length( centre ), 0.0001 ) );
		float forward = sqrt( saturate( 1 - dot( centre, centre ) ) );

		// The surface position and the viewing ray rotated into the same iris frame
		float2 tilt = centre / ( 1 + forward );
		return float3x3(
			float3( 1 - centre.x * tilt.x, -centre.x * tilt.y, -centre.x ),
			float3( -centre.y * tilt.x, 1 - centre.y * tilt.y, -centre.y ),
			float3( centre, forward ) );
	}

	// Human eyes use a uv tile for each eye and hemisphere: the projection repeats, as the original eye textures did
	static float3 EyeProjection( float2 uv, float hemisphere, float3x3 rotation )
	{
		float2 disk = ( frac( uv ) - 0.5 ) / EyeUvRadius;
		float3 sphere = float3( disk, sqrt( saturate( 1 - dot( disk, disk ) ) ) * hemisphere );
		return mul( rotation, sphere ) * EyeUvRadius;
	}

	static float3x3 EyeProjectionFrame( float3 eyeForward, float3 eyeRight, float3x3 rotation )
	{
		float3 forward = normalize( eyeForward );
		float3 right = eyeRight - forward * dot( eyeRight, forward );
		right /= max( length( right ), 0.0001 );
		float3 down = -cross( forward, right );
		return mul( rotation, float3x3( right, down, forward ) );
	}

	// The refracted ray against z = -depth + curvature * radius^2: the iris is below the corneal rim
	static float2 IrisParallaxPoint( float3 surface, float3 ray, float irisRadius )
	{
		float rimHeight = sqrt( max( EyeUvRadius * EyeUvRadius - irisRadius * irisRadius, 0 ) );
		float2 point = surface.xy / irisRadius;
		float2 slope = ray.xy / max( -ray.z, 0.05 );
		float curvature = EyeIrisDepth * EyeIrisConcavity;
		float height = max( ( surface.z - rimHeight ) / irisRadius + EyeIrisDepth - curvature * dot( point, point ), 0 );
		float a = curvature * dot( slope, slope );
		float b = 1 + 2 * curvature * dot( point, slope );
		float root = sqrt( b * b + 4 * a * height );

		// The stable quadratic root, flat irises and head-on views included
		float distance = b >= 0 ? 2 * height / max( b + root, 0.0001 ) : ( root - b ) / max( 2 * a, 0.0001 );
		return point + slope * distance;
	}

	float3 EyeIrisColor( float radius, float pupilMask, IrisTissue tissue )
	{
		float3 baseIrisColor = SrgbGammaToLinear( lerp( IrisColor, g_vAvatarEyeColor.rgb, saturate( g_vAvatarEyeColor.a ) ) );
		float coolPigment = saturate( ( max( baseIrisColor.g, baseIrisColor.b ) - baseIrisColor.r ) / max( max( baseIrisColor.g, baseIrisColor.b ) * 0.6, 0.001 ) );
		float luminance = dot( baseIrisColor, float3( 0.2126, 0.7152, 0.0722 ) );
		baseIrisColor = lerp( baseIrisColor, float3( luminance, luminance, luminance ), coolPigment * 0.3 );
		// Blue irises keep a weaker warm inner zone than brown and green
		float bluePigment = saturate( ( baseIrisColor.b - baseIrisColor.g ) / max( baseIrisColor.b * 0.4, 0.001 ) );
		float innerBlend = tissue.innerZone * lerp( 0.6, 0.2, bluePigment );
		float3 irisColor = baseIrisColor * lerp( float3( 0.95, 1, 1.03 ), float3( 1.6, 0.9, 0.5 ), innerBlend );
		irisColor *= lerp( 1, 0.8, smoothstep( 0.65, 1, radius ) );

		// Pigment thickness changes the colour of the pale tissue under it (an artistic absorption model)
		float3 substrate = lerp( float3( 0.48, 0.42, 0.32 ), float3( 0.30, 0.40, 0.48 ), coolPigment );
		float3 target = max( irisColor * lerp( 0.55, 0.32, coolPigment ), 0.00001 );
		substrate = max( substrate, target );
		float3 absorption = -log( target / substrate );
		float density = exp2( -tissue.fibers * 1.8 - tissue.microFibers * 0.6 - tissue.distanceFibers * 0.35 - tissue.mottling - tissue.pigment * 0.35 );
		irisColor = substrate * exp( -absorption * density );
		irisColor *= 1 - tissue.crypts * 0.22;
		irisColor *= 1 - tissue.furrows * EyeRingStrength * 0.7;
		irisColor *= 1 - tissue.pupilRim * EyePupilRimStrength * 0.85;

		float limbalEdge = 1 - EyeLimbalWidth + tissue.fibers * 0.025;
		float limbalMask = smoothstep( limbalEdge - 0.06, limbalEdge + 0.06, radius );
		irisColor = lerp( irisColor, baseIrisColor * 0.12, limbalMask * EyeLimbalStrength );

		// The pupil is a black opening beneath the reflective cornea
		irisColor *= 1 - pupilMask;
		return irisColor;
	}

	float3 EyeScleraColorAt( float2 uv )
	{
		// Vascular tint grows toward the periphery relative to the iris
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

	// The wet lobe filtered by the normal's variation across the pixel
	static float EyeFilteredRoughness( float roughness, float3 normal )
	{
		float3 dx = ddx( normal );
		float3 dy = ddy( normal );
		float kernelRoughnessSquared = min( 0.5 * ( dot( dx, dx ) + dot( dy, dy ) ), 0.18 );
		return sqrt( saturate( roughness * roughness + kernelRoughnessSquared ) );
	}

	// Nearby lids occlude the wet surface: a horizon test against the depth prepass, scaled by the transverse uv footprint.
	// Outside per-pixel branches, so all four lanes of the quad are there for its derivatives
	static float2 EyeContactVisibility<L : ILayerLighting>( L lighting, float3 position, float3 normal, float3 forward, float2 pixel, float2 uv, float2 viewport )
	{
		float3 dx = ddx( position );
		float3 dy = ddy( position );
		float3 transverseX = dx - forward * dot( dx, forward );
		float3 transverseY = dy - forward * dot( dy, forward );
		float2 ux = ddx( uv );
		float2 uy = ddy( uv );
		float radius = EyeUvRadius * sqrt( ( dot( transverseX, transverseX ) + dot( transverseY, transverseY ) ) / max( dot( ux, ux ) + dot( uy, uy ), 0.00000001 ) );
		float range = max( radius * 1.6, 0.0001 );
		float worldPerPixel = max( length( dx ), length( dy ) );
		float pixelRadius = min( range / max( worldPerPixel, 0.0001 ), 320 );
		float horizonEnergy = 0;
		// Radial samples spread between pixels: fixed distances band parallel to the lids
		float jitter = frac( 52.9829189 * frac( dot( floor( pixel ), float2( 0.06711056, 0.00583715 ) ) ) );

		[unroll]
		for ( int direction = 0; direction < 8; direction++ )
		{
			float angle = ( direction + 0.5 ) * EyeTwoPi / 8;
			float2 axis = float2( cos( angle ), sin( angle ) );
			float horizon = 0;
			[unroll]
			for ( int sampleIndex = 1; sampleIndex <= 4; sampleIndex++ )
			{
				float fraction = ( sampleIndex - frac( jitter + direction * 0.618033989 ) ) / 4.0;
				float2 samplePixel = floor( pixel + axis * max( 1, pixelRadius * fraction * fraction ) ) + 0.5;
				float3 delta = lighting.ScenePosition( samplePixel ) - position;
				float distance = length( delta );
				float elevation = saturate( ( dot( normal, delta ) - radius * 0.02 ) / max( distance, 0.001 ) );
				float falloff = 1 - smoothstep( range * 0.35, range, distance );
				float valid = all( samplePixel > 0 ) && all( samplePixel < viewport ) ? 1.0 : 0.0;
				horizon = max( horizon, elevation * falloff * valid );
			}
			horizonEnergy += horizon * horizon;
		}

		// The four sample patterns of the quad averaged, with no more depth reads
		float2 parity = fmod( floor( pixel ), 2 );
		horizonEnergy += ddx_fine( horizonEnergy ) * ( 0.5 - parity.x );
		horizonEnergy += ddy_fine( horizonEnergy ) * ( 0.5 - parity.y );
		// A calibrated contact response, not a hemispherical visibility integral
		float effectiveHorizon = sqrt( saturate( horizonEnergy / 8 ) );
		// Broad diffuse light is more occluded than the narrow wet reflection
		return max( float2( 0.03, 0.12 ), pow( 1 - effectiveHorizon, float2( 3.5, 1.5 ) ) );
	}

	/// What depth passes and the tools' views read: the geometric normal and the sclera's wet roughness.
	public Material Evaluate( SurfaceInput i, CitizenEyeVaryings v )
	{
		Material m = Material.Init( i );
		m.Albedo = SrgbGammaToLinear( EyeScleraColor ) * i.Color.rgb;
		m.Roughness = EyeScleraRoughness;
		m.Metalness = 0;
		m.Opacity = i.Color.a;
		return m;
	}

	public float3 Shade<L : ILayerLighting>( SurfaceInput i, CitizenEyeVaryings v, L lighting, out Material m )
	{
		float3 position = i.Position;

		// The mesh models a corneal bulge: its normals aren't a sphere's, and they're the optical surface
		float3 normal = i.Normal;
		float3 tangentU = normalize( i.TangentU - normal * dot( normal, i.TangentU ) );
		float handedness = dot( cross( normal, i.TangentU ), i.TangentV ) < 0 ? -1.0 : 1.0;
		float3 tangentV = cross( normal, tangentU ) * handedness;

		float3 cameraToPosition = -i.View;
		float hemisphere = dot( normal, v.Forward ) < 0 ? -1.0 : 1.0;
		float3x3 projectionRotation = EyeProjectionRotation();
		float3x3 projectionFrame = EyeProjectionFrame( v.Forward, v.Right, projectionRotation );
		float3 projection = EyeProjection( i.Uv, hemisphere, projectionRotation );
		float2 uv = projection.xy;
		float irisRadius = EyeIrisRadius();
		float2 surfacePoint = uv / irisRadius;
		float surfaceRadius = length( surfacePoint );
		// The opening stays put as the recessed pattern moves beneath it
		float irisMask = EyeDisc( surfaceRadius - 1, EyeEdgeSoftness );
		// An orthographic projection also has a matching circle on the back of the eye
		float projectionAA = max( fwidth( projection.z ), 0.0001 );
		irisMask *= smoothstep( -projectionAA, projectionAA, projection.z );

		// The corneal curvature across the whole iris, tapered only outside its aperture
		float cornealSlope = 0.3 * ( 1 - smoothstep( 1.0, 1.6, surfaceRadius ) );
		float3 corneaNormal = normalize( normal + mul( float3( surfacePoint * cornealSlope, 0 ), projectionFrame ) );
		float roughness = EyeFilteredRoughness( lerp( EyeScleraRoughness, EyeCorneaRoughness, irisMask ), corneaNormal );

		// Reflection and refraction share the same smooth optical surface
		float3 refracted = refract( cameraToPosition, corneaNormal, 1 / EyeCorneaIor );
		float2 irisPoint = IrisParallaxPoint( projection, mul( projectionFrame, refracted ), irisRadius );
		float radius = length( irisPoint );
		float pupilMask = EyeDisc( radius - EyePupilSize(), 0 );

		float footprint = max( length( ddx( irisPoint ) ), length( ddy( irisPoint ) ) );
		IrisTissue tissue = EvaluateIrisTissue( irisPoint, footprint );
		float3 irisColor = EyeIrisColor( radius, pupilMask, tissue );
		float3 scleraColor = EyeScleraColorAt( uv );
		float3 albedo = lerp( scleraColor, irisColor, irisMask ) * i.Color.rgb;

		// The same bowl the viewing ray meets, with the tissue's relief
		float2 irisSlope = -2 * EyeIrisDepth * EyeIrisConcavity * irisPoint;
		irisSlope += IrisReliefSlope( tissue.height, irisPoint ) * irisMask * ( 1 - pupilMask );
		float3 irisNormal = normalize( mul( float3( irisSlope, 1 ), projectionFrame ) );
		float diffuseWrap = EyeScleraDiffuseWrap * ( 1 - irisMask );
		float diffuseExponent = lerp( 1.5, 1, irisMask );

		// One layer: the tissue's normal for diffuse light, the smooth cornea's for specular, lit a little off the faceted
		// shadow mesh, with this surface's own roughness filtering
		LightLayer cornea = {};
		cornea.Normal = corneaNormal;
		cornea.DiffuseNormal = normalize( lerp( normal, irisNormal, irisMask ) );
		cornea.Roughness = roughness.xx;
		cornea.SpecularColor = EyeCorneaReflectance.xxx;
		cornea.DiffuseWrap = diffuseWrap;
		cornea.DiffuseWrapExponent = diffuseExponent;
		cornea.Offset = normal * 0.03;
		cornea.GeometricRoughness = false;
		LayerLight light = lighting.Light( cornea );

		// Indirect diffuse light (and its occlusion) follows the tissue
		LightLayer tissueLayer = cornea;
		tissueLayer.Normal = cornea.DiffuseNormal;
		tissueLayer.Direct = false;
		LayerLight tissueLight = lighting.Light( tissueLayer );

		// Ambient occlusion: the multi-bounce fit for diffuse (Jimenez et al. 2016), as CalculateDiffuseAmbientOcclusion
		float occlusion = tissueLight.DiffuseOcclusion;
		float3 a = 2.0404 * albedo - 0.3324;
		float3 b = -4.7951 * albedo + 0.6417;
		float3 c = 2.7552 * albedo + 0.6903;
		float3 diffuseOcclusion = max( float3( occlusion, occlusion, occlusion ), ( ( occlusion * a + b ) * occlusion + c ) * occlusion );

		// The lids' contact: the iris's occlusion neutral, the exposed sclera warmed by the light through the lid margin
		float2 contact = EyeContactVisibility( lighting, position, normal, normalize( v.Forward ), i.ScreenPosition.xy, i.Uv, i.ViewportSize );
		float3 contactColor = pow( contact.xxx, lerp( float3( 0.85, 1.0, 1.07 ), float3( 1, 1, 1 ), irisMask ) );

		m = Material.Init( i );
		m.Albedo = albedo;
		m.Normal = corneaNormal;
		m.Roughness = roughness;
		m.Metalness = 0;
		m.Opacity = i.Color.a;

		// Direct reflections already carry their lights' shadows; socket occlusion is the environment's alone
		return albedo * contactColor * ( light.Diffuse + tissueLight.IndirectDiffuse * diffuseOcclusion )
			+ light.Specular + light.IndirectSpecular * light.SpecularOcclusion * contact.y;
	}
}
