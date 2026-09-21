//=========================================================================================================================
// Optional
//=========================================================================================================================
HEADER
{
	Description = "LPV Debugging";
    DevShader = true;
}

//=========================================================================================================================
// Optional
//=========================================================================================================================
FEATURES
{
    #include "common/features.hlsl"
}

//=========================================================================================================================
// Optional
//=========================================================================================================================
MODES
{
    Forward();													// Indicates this shader will be used for main rendering
	ToolsShadingComplexity( "tools_shading_complexity.shader" ); 	// Shows how expensive drawing is in debug view
}

//=========================================================================================================================
COMMON
{
	#include "common/shared.hlsl"

    float flSampleSize < Attribute( "SampleSize" ); Default( 10 ); >;
}

//=========================================================================================================================

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

//=========================================================================================================================

struct PixelInput
{
	#include "common/pixelinput.hlsl"
    float3 vSpherePositionWs : TEXCOORD13;
};

//=========================================================================================================================

VS
{
	#include "common/vertex.hlsl"

	//
	// Main
	//
	PixelInput MainVs( VS_INPUT i )
	{
		PixelInput o = ProcessVertex( i );           

        float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );
        
        //float3 vPositionOs = i.vPositionOs.xyz;
        float3 vPositionOffset = i.vPositionOs;
        float3 vPositionOs = float3( i.vTexCoord.x, -i.vTexCoord.y,  0  ) * flSampleSize * 1.666f;

        float3 vPositionWs = vPositionOffset + Vector3VsToWs( vPositionOs );

        o.vPositionWs = vPositionWs;
        o.vPositionPs.xyzw = Position3WsToPs( o.vPositionWs.xyz );
        
        // Center of spheroid
        o.vSpherePositionWs = vPositionOffset;

		return FinalizeVertex( o );
	}
}

//=========================================================================================================================

PS
{
    #include "common/pixel.hlsl"
	
    RenderState( CullMode, NONE );
    //RenderState( AlphaToCoverageEnable, true );

    float flRoughness < Attribute( "Roughness" ); Default( 1 ); >;
    float flMetalness < Attribute( "Metalness" ); Default( 0 ); >;
    float3 vAlbedo < Attribute( "Albedo" ); Default3( 1, 1, 1 ); >;
    
    // https://www.iquilezles.org/www/articles/spherefunctions/spherefunctions.htm
    float sphIntersect( float3 ro, float3 rd, float4 sph )
    {
        float3 oc = ro - sph.xyz;
        float b = dot( oc, rd );
        float c = dot( oc, oc ) - sph.w*sph.w;
        float h = b*b - c;
        if( h<0.0 ) return -1.0;
        h = sqrt( h );
        return -b - h;
    }
    
    //
	// Main
	//
	float4 MainPs( PixelInput i ) : SV_Target0
	{
        const float fSphereRadius = flSampleSize;
        const float3 vSpherePosition = i.vSpherePositionWs;

        const float4 vSphere = float4( vSpherePosition, fSphereRadius );

        const float3 vRayOrigin = g_vCameraPositionWs;
        const float3 vRayDirection = CalculatePositionToCameraDirWs( g_vCameraPositionWs - i.vPositionWithOffsetWs );

        float fIntersect = sphIntersect( vRayOrigin, vRayDirection, vSphere );
        
        float3 vHitPosition = vRayOrigin + vRayDirection * fIntersect;
        float3 vNormal = normalize( vHitPosition - i.vSpherePositionWs );
        
        i.vPositionWithOffsetWs = vHitPosition - g_vCameraPositionWs;
        i.vNormalWs = vNormal;

		Material m = Material::Init( i );

        m.Roughness = flRoughness;
        m.Metalness = flMetalness;
        m.Albedo = vAlbedo;

        float flOpacity = saturate( smoothstep( g_flNearPlane, g_flNearPlane + 10, fIntersect ) );
        flOpacity = OpaqueFade( flOpacity, i.vPositionSs.xyzw );
		clip( flOpacity - 0.001 );

		return ShadingModelStandard::Shade( i, m );
	}
}