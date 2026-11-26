// citizen_eye's helpers: its constants, the avatar's eye attributes, noise and the procedural iris and sclera
import sbox.surface;

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
[Engine( "eye_color" ), Default4( 0, 0, 0, 0 )] float4 g_vAvatarEyeColor;
[Engine( "eye_size" ), Default( 1 )] float g_flAvatarEyeSize;
[Engine( "eye_align" ), Default2( 0, 0 )] float2 g_vAvatarEyeAlign;
[Engine( "eye_pupil_size" ), Default( 1 )] float g_flAvatarEyePupilSize;


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
