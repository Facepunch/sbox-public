#ifndef LIGHTBINNER_HLSL
#define LIGHTBINNER_HLSL

#include "common/Bindless.hlsl"
#include "math_general.fxc"
#include "common_samplers.fxc"

//-----------------------------------------------------------------------------
// Light Buffer
//-----------------------------------------------------------------------------

enum EnvMapFlags
{
    ProjectionBox,
    ProjectionSphere,
    ProjectionNoParallax
};

enum LightType
{
    LightTypePoint = 1,
    LightTypeDirectional = 2,
    LightTypeSpot = 3,
    LightTypeRect = 4
};

enum LightFlags
{
    Visible = 0x1,                 // visible ( For GPU culling )
    DiffuseEnabled = 0x2,         // diffuse enabled
    SpecularEnabled = 0x4,        // specular enabled
    TransmissiveEnabled = 0x8,    // transmissive enabled
    LightCookieEnabled = 0x40,    // light cookie enabled
    IndexedLight = 0x100,         // indexed baked lighting
    NoFogShadows = 0x200          // skip shadow sampling in volumetric fog
};

enum LightShape
{
    LightShapeSphere = 0,
    LightShapeCapsule = 1,
    LightShapeRectangle = 2
};


//-----------------------------------------------------------------------------

cbuffer ViewLightingConfigV2
{
    int4 NumLights;                     // x - num dynamic lights, y - num baked lights, z - num envmaps, w - num decals

    int4 BakedLightIndexMapping[256];   // Remaps baked light index to the light pool list for fast
                                        // query on the shader, we have a hard limit of 256 baked lights

    float4 AmbientLightColor;           // w = lerp between IBL and flat ambient light

    #define NumDynamicLights        NumLights.x
    #define NumBakedIndexedLights   NumLights.y
    #define NumEnvironmentMaps      NumLights.z
    #define NumDecals               NumLights.w
};

//
// matt: I could spend all day refactoring this struct/file
//       there's too much happening here
//       light cookies should be separate the same way as shadows are now
//       treating data like a class is wrong, there's a lot of redundant data
//       envmaps should be in their own file
//

class BinnedLight
{
    uint Type;          // 1 = spot, 2 = point, 3 = rect, etc..
    LightShape Shape;   // Sphere, Capsule, Rectangle, etc..., maybe redundant with Type?
    uint Flags;

    float4x4 LightToWorld;

    float3 Color;

    float LinearFalloff;
    float QuadraticFalloff;
    float FalloffBias;
    float Radius;
    float RadiusSquared;
    float2 ShapeSize;

    float4 SpotLightInnerOuterConeCosines; // x - inner cone, y - outer cone, z - reciprocal between inner and outer angle, w - Tangent of outer angle


    float FogIntensity;

    // Index in the shadow array, 0xFFFFFFFF if no shadow
    uint ShadowMapIndex;

    // Light Cookies, fancy image projection on light, 0xFFFFFFFF if no cookie
    uint LightCookieTextureIndex;

    // Custom shadow techniques precomputed on compute shader, RT, Screenspace shadows, Capsules, etc, 0xFFFFFFFF if no shadow mask, otherwise index in bindless array
    uint ShadowMaskTextureIndex;
    
	// ---------------------------------

	float3 GetPosition() 			{ return LightToWorld[3].xyz; }
	float3 GetDirection() 			{ return LightToWorld[0].xyz; }
	float3 GetDirectionUp() 		{ return LightToWorld[1].xyz; }
    // w t f
    bool IsSpotLight()              { return ( SpotLightInnerOuterConeCosines.x != 0.0f ); }

    bool IsDiffuseEnabled()             { return ( Flags & LightFlags::DiffuseEnabled ) != 0; }
    bool IsSpecularEnabled()            { return ( Flags & LightFlags::SpecularEnabled ) != 0; }
	bool IsTransmissiveEnabled() 	    { return ( Flags & LightFlags::TransmissiveEnabled ) != 0; }
    bool HasDynamicShadows() 	        { return ( ShadowMapIndex != 0xFFFFFFFF ); }
    bool HasLightCookie()               { return ( Flags & LightFlags::LightCookieEnabled ) != 0; }
	bool HasFogShadows()                { return ( Flags & LightFlags::NoFogShadows ) == 0; }
    bool IsIndexedLight()               { return ( Flags & LightFlags::IndexedLight ) != 0; }

    Texture2D GetLightCookieTexture()   { return g_bindless_Texture2D[ LightCookieTextureIndex ]; }

    float4 SampleLightCookie( float2 uv, float level = 0.0f )
    {
        return GetLightCookieTexture().SampleLevel( g_sTrilinearClamp, uv, level );
    }

    // Transpose of LightToWorld rotation = WorldToLight (orthonormal basis).
    // posLS.x = depth along forward, posLS.yz = lateral.
    float2 GetCookieUV( float3 vPositionWs )
    {
        float3 posLS = mul( (float3x3)LightToWorld, vPositionWs - GetPosition() );
        float2 ndc = posLS.yz / ( posLS.x * SpotLightInnerOuterConeCosines.w );
        return ndc * float2( 0.5, -0.5 ) + 0.5;
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

StructuredBuffer<BinnedLight>    BinnedLightBufferV2    < Attribute( "BinnedLightBufferV2" );  > ;
StructuredBuffer<BinnedEnvMap>   BinnedEnvMapBuffer   < Attribute( "BinnedEnvMapBuffer" ); > ;

BinnedLight DynamicLightConstantByIndex( int index )
{
    return BinnedLightBufferV2[ index ];
}

BinnedLight BakedIndexedLightConstantByIndex( int index )
{
    return BinnedLightBufferV2[ BakedLightIndexMapping[index].x ];
}

BinnedEnvMap EnvironmentMapConstantByIndex( int index )
{
    return BinnedEnvMapBuffer[ index ];
}

//-----------------------------------------------------------------------------

#include "common/classes/ClusterCulling.hlsl"

//-----------------------------------------------------------------------------

#endif // LIGHTBINNER_HLSL