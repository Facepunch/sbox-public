//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Resolves a depth copy from one texture to another, doesnt care about dest format.";
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
	#include "system.fxc" // This should always be the first include in COMMON
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	#include "common.fxc"

	struct VS_INPUT
	{
		float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
		float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
	};

	struct VS_OUTPUT
	{
		float4 vPositionPs : SV_Position;
		float2 vTexCoord : TEXCOORD0;
	};

	VS_OUTPUT MainVs( VS_INPUT i )
	{
		VS_OUTPUT o;
		o.vPositionPs = float4( i.vPositionOs.xyz, 1.0 );
		o.vTexCoord = i.vTexCoord;
		return o;
	}
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	#include "common.fxc"
	#define floatx float

	DynamicCombo( D_MSAA, 0..1, Sys( ALL ) );

#if D_MSAA
	Texture2DMS<float>  g_tSourceDepth  < Attribute( "SourceDepth" ); >;
#else
	Texture2D<float>  g_tSourceDepth  < Attribute( "SourceDepth" ); >;
#endif

	int DownsampleFactor < Attribute( "DownsampleFactor" ); Default( 1 ); >;

	floatx MainPs( float4 vPositionPs : SV_Position, float2 vTexCoord : TEXCOORD0 ) : SV_Depth
	{
		// Calculate source coordinate with proper downsampling
		uint2 sourceCoord = (g_vViewportOffset.xy + uint2(vPositionPs.xy)) * DownsampleFactor;
		
		floatx result;
		#if D_MSAA
		{
			result = g_tSourceDepth.Load( sourceCoord, 0 ).r;
		}
		#else
		{
			result = g_tSourceDepth.Load( int3( sourceCoord, 0 ) ).r;
		}
		#endif
 
		return result;
	}
}
