//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Converts reactive depth buffer into R16F mask for FSR3";
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

	Texture2D<float> g_tReactiveDepth < Attribute( "ReactiveDepth" ); SrgbRead( false ); >;

	float4 MainPs( float4 vPositionSs : SV_Position ) : SV_Target0
	{
		float flDepth = g_tReactiveDepth.Load( int3( int2( vPositionSs.xy ), 0 ) ).r;

		// Reverse-Z: clear = 0.0 (far). Any depth > 0 means a reactive object was rendered here.
		float flReactive = flDepth > 0.001 ? 1.0 : 0.0;
		return float4( flReactive, 0, 0, 0 );
	}
}
