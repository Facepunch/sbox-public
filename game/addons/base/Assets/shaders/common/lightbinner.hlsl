#ifndef LIGHTBINNER_HLSL
#define LIGHTBINNER_HLSL

#include "common/Bindless.hlsl"
#include "math_general.fxc"
#include "common_samplers.fxc"
#include "sheet_sampling.fxc"

//-----------------------------------------------------------------------------
// Light Buffer
//-----------------------------------------------------------------------------

enum EnvMapFlags
{
    ProjectionBox,
    ProjectionSphere,
    ProjectionNoParallax
};

enum LightFlags
{
    Visible = 0x1,                 // visible ( For GPU culling )
    DiffuseEnabled = 0x2,         // diffuse enabled
    SpecularEnabled = 0x4,        // specular enabled
    TransmissiveEnabled = 0x8,    // transmissive enabled
    ShadowEnabled = 0x10,         // shadow enabled
    ScreenspaceShadows = 0x20,    // screenspace shadows enabled
    LightCookieEnabled = 0x40,    // light cookie enabled
    FrustumFeathering = 0x80,     // frustum feathering enabled
    IndexedLight = 0x100,         // indexed baked lighting
    NoFogShadows = 0x200          // skip shadow sampling in volumetric fog
};

enum LightShape
{
    Sphere = 0,
    Capsule = 1,
    Rectangle = 2
};

#define MAX_SHADOW_FRUSTA_PER_LIGHT 6

//-----------------------------------------------------------------------------

cbuffer ViewLightingConfig
{
    int4 ViewLightingFlags;
    int4 NumLights;                     // x - num dynamic lights, y - num baked lights, z - num fog lights, w - num envmaps 

    int4 BakedLightIndexMapping[256];   // Remaps baked lights index to the light pool list for fast
                                        // query on the shader, we have a hard limit of 256 baked lights

    float4 Shadow3x3PCFConstants[4]; // float4( 1.0 / 267.0, 7.0 / 267.0, 4.0 / 267.0, 20.0 / 267.0 );
                                     // float4( 33.0 / 267.0, 55.0 / 267.0, -flTexelEpsilon, 0.0 );
                                     // float4( flTwoTexelEpsilon, -flTwoTexelEpsilon, 0.0, flTexelEpsilon );
                                     // float4( flTexelEpsilon, -flTexelEpsilon, flTwoTexelEpsilon, -flTwoTexelEpsilon );

    float4 EnvironmentMapSizeConstants; // x = size, y = log2( size ) - 3, z = log2( size ), all envmaps are the same size, so only one copy of SizeConstants.
    float4 LegacyAmbientLightColor[ 3 ];
    float4 AmbientLightColor;           // w = lerp between IBL and flat ambient light
    
    #define NumDynamicLights        NumLights.x
    #define NumBakedIndexedLights   NumLights.y
    #define NumEnvironmentMaps      NumLights.z
    #define NumDecals               NumLights.w
};

//-----------------------------------------------------------------------------

Texture2D g_BinnedLightSheetTexture < Attribute( "SheetTexture" ); SrgbRead( false ); >;

class BinnedLight
{
    uint4 Params;                           // x = num sequential frusta, y = cookie texture index, z = flags, w = light type
    float4 Color;                          // w - Linear radius
    float4 FalloffParams;                  // x - Linear falloff, y - quadratic falloff, z - radius squared for culling, w - truncation
    float4 SpotLightInnerOuterConeCosines; // x - inner cone, y - outer cone, z - reciprocal between inner and outer angle, w - Tangent of outer angle
    float4 Shape;                          // xy - size,  zw - unused

    float4x4 LightToWorld;

    // Shadow
    float4 ShadowBounds[ MAX_SHADOW_FRUSTA_PER_LIGHT ];
    float4 LightCookieSheet;
	float4 ExtraData;
	float4 Unused2;
	float4 Unused3;
	float4 Unused4;
	float4 Unused5;
    float4x4 WorldToShadow[ MAX_SHADOW_FRUSTA_PER_LIGHT ];
    float4 ProjectedShadowDepthToLinearDepth; // Assume all frusta have the same depth range

    float4x4 WorldToLightCookie; // Could just be world to light now, we are bindless! Still could use it for stuff like cloud layers if we keep separate though

	// ---------------------------------

    uint    NumShadowFrusta()      { return Params.x; }
    
	float3 GetPosition() 			{ return LightToWorld[3].xyz; }
	float3 GetDirection() 			{ return LightToWorld[0].xyz; }
	float3 GetDirectionUp() 		{ return LightToWorld[1].xyz; }
	float3 GetColor() 			    { return Color.xyz; }

	float GetLinearFalloff() 		{ return FalloffParams.x; }
	float GetQuadraticFalloff() 	{ return FalloffParams.y; }
	float GetRadiusSquared() 		{ return FalloffParams.z; }
	float GetRadius() 		        { return Color.w; }

	float2 GetShapeSize() 			{ return Shape.xy; }
	LightShape GetShape() 			{ return (LightShape)Params.w; }

    bool IsSpotLight()              { return ( SpotLightInnerOuterConeCosines.x != 0.0f ); }

	// ---------------------------------

    bool IsVisible()                    { return ( Params.z & LightFlags::Visible ) != 0; }
    bool IsDiffuseEnabled()             { return ( Params.z & LightFlags::DiffuseEnabled ) != 0; }
    bool IsSpecularEnabled()            { return ( Params.z & LightFlags::SpecularEnabled ) != 0; }
	bool IsTransmissiveEnabled() 	    { return ( Params.z & LightFlags::TransmissiveEnabled ) != 0; }
    bool HasDynamicShadows() 	        { return ( Params.z & LightFlags::ShadowEnabled ) != 0; }
    bool HasLightCookie()               { return ( Params.z & LightFlags::LightCookieEnabled ) != 0; }
    bool HasFrustumFeathering()         { return ( Params.z & LightFlags::FrustumFeathering ) != 0; }
    bool IsIndexedLight()               { return ( Params.z & LightFlags::IndexedLight ) != 0; }

	float GetFogScale()    { return ExtraData.x; }
	bool HasFogShadows()   { return ( Params.z & LightFlags::NoFogShadows ) == 0; }
    
    Texture2D GetLightCookieTexture()   { return g_bindless_Texture2D[ Params.y ]; }

    float4 SampleLightCookie( float2 uv, float level = 0.0f )
    {
        Texture2D texture = GetLightCookieTexture();

        float blend;
        float2 a;
        float2 b;
        GetLightCookieUV( uv, a, b, blend );

        float4 col = texture.SampleLevel( g_sTrilinearClamp, a, level );

		if ( blend > 0 )
		{
			float4 col2 = texture.SampleLevel( g_sTrilinearClamp, b, level );
			col = lerp( col, col2, blend );
		}

        return col;
    }

    bool GetLightCookieUV( float2 uv, out float2 a, out float2 b, out float blend )
    {
        a = uv;
		b = uv;
		blend = 0;

        float4 data = LightCookieSheet;   
		if ( data.w == 0 )
			return false;

        SheetDataSamplerParams_t params;
		params.m_flSheetTextureBaseV = data.x;
		params.m_flOOSheetTextureWidth = 1.0f / data.y;
		params.m_flOOSheetTextureHeight = data.z;
		params.m_flSheetTextureWidth = data.y;
		params.m_flSheetSequenceCount = data.w;
		params.m_flSequenceAnimationTimescale = 1.0f;
		params.m_flSequenceIndex = 0;
		params.m_flSequenceAnimationTime = g_flTime;

		SheetDataSamplerOutput_t o = SampleSheetData( g_sPointWrap, g_BinnedLightSheetTexture, params, false );
	
		o.m_vFrame0Bounds.zw -= o.m_vFrame0Bounds.xy;
		a = o.m_vFrame0Bounds.xy + ( uv * o.m_vFrame0Bounds.zw );

		o.m_vFrame1Bounds.zw -= o.m_vFrame1Bounds.xy;
		b = o.m_vFrame1Bounds.xy + ( uv * o.m_vFrame1Bounds.zw );
		
		blend = o.m_flAnimationBlendValue;

        return true;
    }
};

class BinnedEnvMap
{
    float4x3 WorldToLocal;
    float4 BoxMins;
    float4 BoxMaxs;
    float4 Color; // w - feathering
    float4 NormalizationSH; // Unused
    uint4   Attributes; // x = cubemap texture index, y = flags (future), z = unused, w = unused

    // ---------------------------------

    uint GetCubemapIndex() { return Attributes.x; }
};


//-----------------------------------------------------------------------------

bool IsLightMapDirectionalityDisabled()
{
    return false; //(LightDataFlags.x & LIGHTDATA_FLAGS_NO_LIGHTMAP_DIRECTIONALITY) != 0;
}

//-----------------------------------------------------------------------------

StructuredBuffer<BinnedLight>    BinnedLightBuffer    < Attribute( "BinnedLightBuffer" );  > ;
StructuredBuffer<BinnedEnvMap>   BinnedEnvMapBuffer   < Attribute( "BinnedEnvMapBuffer" ); > ;

BinnedLight DynamicLightConstantByIndex( int index )
{
    return BinnedLightBuffer[ index ];
}

BinnedLight BakedIndexedLightConstantByIndex( int index )
{
    return BinnedLightBuffer[ BakedLightIndexMapping[index].x ];
}

BinnedEnvMap EnvironmentMapConstantByIndex( int index )
{
    return BinnedEnvMapBuffer[ index ];
}

//-----------------------------------------------------------------------------

#include "common/classes/ClusterCulling.hlsl"

//-----------------------------------------------------------------------------

#endif // LIGHTBINNER_HLSL