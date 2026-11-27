#ifndef BINNEDLIGHT_HLSL
#define BINNEDLIGHT_HLSL

// A light as the light binner packs it (RenderTools.PackSceneLight). Its cookie needs the bindless set, so a shader
// without it, like one dispatched outside a view, reads lights without their cookies

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

struct BinnedLight
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

    uint LightModulatorOffset;
    uint LightModulatorCount;
    
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

#ifdef BINDLESS_H
    Texture2D GetLightCookieTexture()   { return Bindless::GetTexture2D( LightCookieTextureIndex ); }

    float4 SampleLightCookie( float2 uv, float level = 0.0f )
    {
        return GetLightCookieTexture().SampleLevel( g_sTrilinearClamp, uv, level );
    }
#endif

    // Transpose of LightToWorld rotation = WorldToLight (orthonormal basis).
    // posLS.x = depth along forward, posLS.yz = lateral.
    float2 GetCookieUV( float3 vPositionWs )
    {
        float3 posLS = mul( (float3x3)LightToWorld, vPositionWs - GetPosition() );
        float2 ndc = posLS.yz / ( posLS.x * SpotLightInnerOuterConeCosines.w );
        return ndc * float2( 0.5, -0.5 ) + 0.5;
    }
};

#endif // BINNEDLIGHT_HLSL
