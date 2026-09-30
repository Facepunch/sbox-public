//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Visualize FSR3 reactive mask as red overlay";
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Forward();
	Default();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "system.fxc"
	#include "common.fxc"
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	struct VS_INPUT
	{
		float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
		float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
	};

	struct VS_OUTPUT
	{
		float4 vPositionPs : SV_Position;
	};

	VS_OUTPUT MainVs( VS_INPUT i )
	{
		VS_OUTPUT o;
		o.vPositionPs = float4( i.vPositionOs.xyz, 1.0 );
		return o;
	}
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	RenderState( DepthWriteEnable, false );
	RenderState( DepthEnable, false );

	Texture2D<float> g_tReactiveMask < Attribute( "ReactiveMask" ); SrgbRead( false ); >;

	float4 MainPs( float4 vPositionSs : SV_Position ) : SV_Target0
	{
		float flReactive = g_tReactiveMask.Load( int3( int2( vPositionSs.xy ), 0 ) ).r;

		// Reactive pixels shown as red, non-reactive as dark grey scene silhouette
		float3 color = lerp( float3( 0.1, 0.1, 0.1 ), float3( 1.0, 0.0, 0.0 ), saturate( flReactive ) );
		return float4( color, 1.0 );
	}
}
