// foliage.shader written as a surface, by hand: its PS body is Evaluate, its wind is Displace and its wrap and
// translucency from the sun are Light (ISunLitSurface: once, outside the light loop, as foliage.shader does). Its features
// are fields a .vmat sets: alpha test (with today's alpha to coverage) and back faces through the render state, the wind
// (F_FOLIAGE_ANIMATION), translucency (F_TRANSMISSIVE) and the grazing fade (F_GRAZING_FADE) switch their terms.
//
// foliage.shader's colour pass tests depth for equality without writing it (after the prepass); this one tests
// greater-or-equal, which draws the same pixels.
import sbox.surface;

// What the material compiler makes of a .vmat's images (SlangMaterialCompiler): foliage.shader's texture inputs,
// the textures made from them and its attributes, read with the material's features
#if MATERIAL_INPUTS
// The material editor's FEATURES lines, as the .shader declared them
#include "common/features.hlsl"
Feature( F_ALPHA_TEST, 0..1, "Rendering" );
Feature( F_FOLIAGE_ANIMATION, 0..2( 0 = "None", 1 = "Vertex Color Based", 2 = "Trunk Only" ), "Foliage Animation" );
Feature( F_TRANSMISSIVE, 0..1, "Rendering" );
Feature( F_GRAZING_FADE, 0..1, "Rendering" );
// The material's parameters as the material editor shows them, with the feature states that use them
bool g_bFogEnabled < Default( 1 ); UiGroup( "Fog,300" ); >;
float g_flAlphaDistanceEnd < Default( 2000 ); Range( 0, 10000 ); UiGroup( "Alpha Test" ); ActiveIf( F_ALPHA_TEST != 0 ); >;
float g_flAlphaDistanceStart < Default( 500 ); Range( 0, 5000 ); UiGroup( "Alpha Test" ); ActiveIf( F_ALPHA_TEST != 0 ); >;
float g_flAlphaTestReference < Default( 0.5 ); Range( 0.01, 0.99 ); UiGroup( "Translucent" ); ActiveIf( F_ALPHA_TEST != 0 ); >;
float g_flAmbientBoost < Default( 0 ); Range( 0, 0.5 ); UiGroup( "Foliage" ); >;
float g_flBranchAmplitude < Default( 0.1 ); Range( 0, 1 ); UiGroup( "Foliage Animation,10/Detail" ); ActiveIf( F_FOLIAGE_ANIMATION == 1 ); >;
float g_flBranchFrequency < Default( 0.5 ); Range( 0.1, 5 ); UiGroup( "Foliage Animation,10/Detail" ); ActiveIf( F_FOLIAGE_ANIMATION == 1 ); >;
float g_flAntiAliasedEdgeStrength < Default( 1 ); Range( 0, 1 ); UiGroup( "Translucent" ); ActiveIf( F_ALPHA_TEST != 0 ); >;
float g_flDetailFadeDistance < Default( 500 ); Range( 100, 2000 ); UiGroup( "Foliage" ); >;
float g_flEdgeAmplitude < Default( 0.1 ); Range( 0, 1 ); UiGroup( "Foliage Animation,10/Detail" ); ActiveIf( F_FOLIAGE_ANIMATION == 1 ); >;
float g_flEdgeFrequency < Default( 1 ); Range( 0.1, 5 ); UiGroup( "Foliage Animation,10/Detail" ); ActiveIf( F_FOLIAGE_ANIMATION == 1 ); >;
float g_flGrassNormalUp < Default( 0 ); Range( 0, 1 ); UiGroup( "Foliage" ); >;
float g_flGrazingFadeEnd < Default( 0.1 ); Range( 0, 1 ); UiGroup( "Grazing Fade" ); ActiveIf( F_GRAZING_FADE != 0 ); >;
float g_flGrazingFadeStart < Default( 0.5 ); Range( 0, 1 ); UiGroup( "Grazing Fade" ); ActiveIf( F_GRAZING_FADE != 0 ); >;
float g_flMinRoughness < Default( 0.5 ); Range( 0, 1 ); UiGroup( "Foliage" ); >;
float g_flNormalVariation < Default( 0.1 ); Range( 0, 0.5 ); UiGroup( "Foliage" ); >;
float g_flRimPower < Default( 4 ); Range( 1, 8 ); UiGroup( "Foliage" ); >;
float g_flRimStrength < Default( 0 ); Range( 0, 2 ); UiGroup( "Foliage" ); >;
float g_flSpecularOcclusion < Default( 0 ); Range( 0, 1 ); UiGroup( "Foliage" ); >;
float g_flSwaySpeed < Default( 1 ); Range( 0, 10 ); UiGroup( "Foliage Animation,20/Trunk" ); ActiveIf( F_FOLIAGE_ANIMATION != 0 ); >;
float g_flSwayStrength < Default( 1 ); Range( 0, 25 ); UiGroup( "Foliage Animation,20/Trunk" ); ActiveIf( F_FOLIAGE_ANIMATION != 0 ); >;
float3 g_flTintColor < UiType( Color ); Default3( 1, 1, 1 ); UiGroup( "Material,10/90" ); >;
float g_flTransmissionScale < Default( 1 ); Range( 0, 10 ); UiGroup( "Transmissive" ); ActiveIf( F_TRANSMISSIVE != 0 ); >;
float g_flWrapAmount < Default( 0.5 ); Range( 0, 1 ); UiGroup( "Foliage" ); ActiveIf( F_TRANSMISSIVE != 0 ); >;
float g_flWrapStrength < Default( 0.3 ); Range( 0, 1 ); UiGroup( "Foliage" ); ActiveIf( F_TRANSMISSIVE != 0 ); >;
BoolAttribute( NeedsLightProbe, true );
#if ( F_ALPHA_TEST )
BoolAttribute( alphatest, true );
#endif
// Its wind moves vertices: lights draw it live instead of keeping it in their static shadow caches
#if ( F_FOLIAGE_ANIMATION )
BoolAttribute( AnimatesVertices, true );
#endif
// End of the material editor's lines
#define S_FOLIAGE_ANIMATION F_FOLIAGE_ANIMATION
#include "common/vertex.hlsl"
BoolAttribute( VertexNeedsPropOrigin, true );
#include "common/utils/Material.CommonInputs.hlsl"
#include "common/pixel.hlsl"
#define S_ALPHA_TEST F_ALPHA_TEST
#define S_TRANSMISSIVE F_TRANSMISSIVE
#define S_GRAZING_FADE F_GRAZING_FADE
#if S_ALPHA_TEST
TextureAttribute( LightSim_Opacity_A, g_tColor );
#endif
#if S_TRANSMISSIVE
CreateInputTexture2D( TextureTransmissiveColor, Srgb, 8, "", "_color", "Material,10/60", Default3( 1.0, 1.0, 1.0 ) );
Texture2D g_tTransmissiveColor < Channel( RGB, Box( TextureTransmissiveColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
#endif
// foliage.shader's vertex inputs (its VS_INPUT, every dynamic combo's): what the model compiler lays out the meshes of
// models using these materials by (the material's VSInputSignature), so they keep the streams they had
#define VS_INPUT_HAS_TANGENT_BASIS 1
struct VS_INPUT
{
	#include "common/vertexinput.hlsl"
	#if ( S_FOLIAGE_ANIMATION == 1 )
		float4 vColor : COLOR0 < Semantic( Color ); >;
	#endif
};
#endif

[Material]
public struct Foliage : IVertexSurface, ISunLitSurface
{
	// Features
	[Group( "Features" ), Source( "F_ALPHA_TEST" ), RenderState( "AlphaTest" )] bool UseAlphaTest;
	[Group( "Features" ), Source( "F_RENDER_BACKFACES" ), RenderState( "TwoSided" )] bool RenderBackfaces;

	// Material.CommonInputs.hlsl's textures and tint
	[Group( "Material" ), Srgb, Opacity] Texture2D Color;  // rgb albedo, a opacity
	[Group( "Material" )] Texture2D Normal;                // rgb tangent normal, a tint mask
	[Group( "Material" )] Texture2D Rma;                   // roughness, metalness, occlusion, transmission
	[Group( "Material" ), Default3( 1, 1, 1 )] float3 TintColor;

	// Foliage
	[Group( "Foliage" ), Range( 0, 1 ), Default( 0.5 )] float WrapAmount;
	[Group( "Foliage" ), Range( 0, 1 ), Default( 0.3 )] float WrapStrength;
	[Group( "Foliage" ), Range( 0, 2 ), Default( 0 )] float RimStrength;
	[Group( "Foliage" ), Range( 1, 8 ), Default( 4 )] float RimPower;
	[Group( "Foliage" ), Range( 0, 0.5 ), Default( 0 )] float AmbientBoost;
	[Group( "Foliage" ), Range( 100, 2000 ), Default( 500 )] float DetailFadeDistance;
	[Group( "Foliage" ), Range( 0, 1 ), Default( 0.5 )] float MinRoughness;
	[Group( "Foliage" ), Range( 0, 0.5 ), Default( 0.1 )] float NormalVariation;
	[Group( "Foliage" ), Range( 0, 1 ), Default( 0 )] float GrassNormalUp;

	// Wrap lighting and translucency (F_TRANSMISSIVE)
	[Group( "Transmissive" ), Source( "F_TRANSMISSIVE" )] bool Transmissive;
	[Group( "Transmissive" ), Srgb] Texture2D TransmissiveColor;
	[Group( "Transmissive" ), Range( 0, 10 ), Default( 1 )] float TransmissionScale;

	// The alpha test's cutoff (g_flAlphaTestReference), relaxed with distance so foliage doesn't pop as its LODs change
	[Group( "Alpha Test" ), Range( 0.01, 0.99 ), Default( 0.5 )] float AlphaTestReference;
	[Group( "Alpha Test" ), Range( 0, 5000 ), Default( 500 )] float AlphaDistanceStart;
	[Group( "Alpha Test" ), Range( 0, 10000 ), Default( 2000 )] float AlphaDistanceEnd;

	// Leaf cards fade out as they turn edge-on (F_GRAZING_FADE)
	[Group( "Grazing Fade" ), Source( "F_GRAZING_FADE" )] bool GrazingFade;
	[Group( "Grazing Fade" ), Range( 0, 1 ), Default( 0.5 )] float GrazingFadeStart;
	[Group( "Grazing Fade" ), Range( 0, 1 ), Default( 0.1 )] float GrazingFadeEnd;

	// Wind (F_FOLIAGE_ANIMATION): vertex colour red and green attenuate edge and branch bending, blue is the phase
	[Group( "Foliage Animation" ), Source( "F_FOLIAGE_ANIMATION" )] bool Animated;
	[Group( "Foliage Animation" ), Range( 0.1, 5 ), Default( 1 )] float EdgeFrequency;
	[Group( "Foliage Animation" ), Range( 0, 1 ), Default( 0.1 )] float EdgeAmplitude;
	[Group( "Foliage Animation" ), Range( 0.1, 5 ), Default( 0.5 )] float BranchFrequency;
	[Group( "Foliage Animation" ), Range( 0, 1 ), Default( 0.1 )] float BranchAmplitude;
	[Group( "Foliage Animation" ), Range( 0, 25 ), Default( 1 )] float SwayStrength;
	[Group( "Foliage Animation" ), Range( 0, 10 ), Default( 1 )] float SwaySpeed;

	public Material Evaluate( SurfaceInput i )
	{
		float4 color = i.Sample( Color, i.Uv );
		float4 normal = i.Sample( Normal, i.Uv );
		float4 rma = i.Sample( Rma, i.Uv );

		Material m = {};
		m.Albedo = color.rgb * TintColor * i.Color.rgb;
		m.Opacity = color.a;
		m.Metalness = rma.g;
		m.Roughness = max( rma.r, MinRoughness );
		m.AmbientOcclusion = rma.b;
		m.Transmission = rma.a;
		m.TintMask = normal.a;

		m.Normal = i.NormalFromMap( normal.xyz );
		m.Normal = normalize( lerp( m.Normal, float3( 0, 0, 1 ), GrassNormalUp ) );

		if ( NormalVariation > 0.0 )
		{
			float2 cell = floor( i.Uv * 2.0 );
			float angle = frac( sin( dot( cell, float2( 12.9898, 78.233 ) ) ) * 43758.5453 ) * 6.283;
			m.Normal = normalize( m.Normal + i.TangentU * cos( angle ) * NormalVariation + i.TangentV * sin( angle ) * NormalVariation );
		}

		// The material's cutoff relaxed with distance, against the cutoff the pass tests with (i.AlphaReference): opacity
		// scaled by their ratio, which the sharpening's derivatives cancel, so the edge falls where the material's cutoff puts it
		float distance = saturate( ( i.ViewDistance - AlphaDistanceStart ) / max( AlphaDistanceEnd - AlphaDistanceStart, 0.001 ) );
		m.Opacity *= i.AlphaReference / max( lerp( AlphaTestReference, 0.1, distance ), 0.001 );

		// Edge-on cards fade as whole triangles: on the coverage, after the cutoff
		if ( GrazingFade )
			m.Coverage = saturate( ( abs( dot( i.FaceNormal, i.View ) ) - GrazingFadeEnd ) / max( GrazingFadeStart - GrazingFadeEnd, 0.001 ) );

		// Rim light close up, and an ambient boost, as emission
		if ( RimStrength > 0.0 && i.ViewDistance < DetailFadeDistance * 10.0 )
			m.Emission += m.Albedo * pow( 1.0 - saturate( dot( m.Normal, i.View ) ), RimPower ) * RimStrength;

		if ( AmbientBoost > 0.0 )
			m.Emission += m.Albedo * AmbientBoost;

		return m;
	}

	// Wrap diffuse past the terminator, and back-scatter translucency through the leaf, from the sun: foliage.shader's cluster
	// light loop is commented out. Lighting every light would make it an ILitSurface, at a register cost in the light loop
	public float3 Light( Material m, SurfaceInput i, LightSample light )
	{
		if ( !Transmissive ) return 0;

		const float boost = 1.5;
		float scatterShadow = lerp( 0.2, 1.0, light.Visibility );
		float3 result = 0;

		if ( WrapStrength > 0.0 )
		{
			float ndl = dot( m.Normal, light.Direction );
			float wrapped = saturate( ( ndl + WrapAmount ) / ( 1.0 + WrapAmount ) );
			result += m.Albedo * light.Color * max( wrapped - saturate( ndl ), 0.0 ) * WrapStrength * light.Attenuation * boost;
		}

		{
			float3 tint = i.Sample( TransmissiveColor, i.Uv ).rgb;
			float3 scatter = normalize( light.Direction + m.Normal * 0.2 );
			float backlit = pow( saturate( dot( i.View, -scatter ) ), 3.0 );
			result += m.Albedo * tint * light.Color * ( backlit + 0.1 ) * light.Attenuation * scatterShadow * TransmissionScale * boost;
		}

		return result;
	}

	// Trunk bending (common/trunk_bending.hlsl) then detail bending (foliage.shader's FoliageDetailBending)
	public float3 Displace( VertexSurfaceInput v )
	{
		float3 position = v.Position;
		if ( !Animated ) return position;

		float bend = position.z * SwayStrength * 0.0001;
		bend *= bend;
		float windStrength = length( v.Wind );
		float3 windDirection = windStrength > 0.01 ? v.Wind / windStrength : float3( 1, 0, 0 );
		float sway = sin( v.Time * SwaySpeed ) + sin( v.Time * SwaySpeed * 0.7 ) * 0.5;
		position.xy += windDirection.xy * bend * ( windStrength + sway );

		const float4 frequencies = float4( 1.975, 0.793, 0.375, 0.193 );
		float branchPhase = v.Color.b + dot( v.ObjectOrigin, 1 );
		float vertexPhase = dot( position, v.Color.b + branchPhase );
		float2 phase = fmod( float2( vertexPhase, branchPhase ), 50000.0 );
		float4 waves = frac( ( v.Time * float2( EdgeFrequency, BranchFrequency ) + phase ).xxyy * frequencies ) * 2.0 - 1.0;
		waves = abs( frac( waves + 0.5 ) * 2.0 - 1.0 );
		waves = waves * waves * ( 3.0 - 2.0 * waves );
		float2 sum = waves.xz + waves.yw;

		float windIntensity = saturate( windStrength * 0.2 );
		float branchBend = 1.0 - abs( dot( normalize( v.Wind + 0.001 ), normalize( float3( position.xy, 0.0 ) + 0.001 ) ) );
		branchBend *= branchBend;

		position += sum.x * v.Color.r * EdgeAmplitude * windIntensity * v.Normal;
		position += sum.y * v.Color.g * BranchAmplitude * windIntensity * float3( 0, 0, 1 );
		position += sum.y * v.Color.g * BranchAmplitude * branchBend * windIntensity * v.Wind;
		return position;
	}
}
