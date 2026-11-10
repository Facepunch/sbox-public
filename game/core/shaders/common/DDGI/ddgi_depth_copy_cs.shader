//---------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Copy depth texture to color texture array slice for DDGI probe rendering";
}

//---------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
}

//---------------------------------------------------------------------------------------------------------------------
FEATURES
{
}

//---------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "system.fxc"
}

//---------------------------------------------------------------------------------------------------------------------
CS
{
	Texture2D<float> SourceDepth < Attribute( "SourceDepth" ); >;
	RWTexture2DArray<float> DestTextureArray < Attribute( "DestTextureArray" ); >;
	
	int2 TextureSize < Attribute( "TextureSize" ); >;
	int DestArraySlice < Attribute( "DestArraySlice" ); Default( 0 ); >;
	
	[numthreads( 8, 8, 1 )]
	void MainCs( uint3 vThreadId : SV_DispatchThreadID )
	{
		if ( vThreadId.x >= (uint)TextureSize.x || vThreadId.y >= (uint)TextureSize.y )
			return;
		
		float depth = SourceDepth.Load( int3( vThreadId.xy, 0 ) );
		DestTextureArray[int3( vThreadId.xy, DestArraySlice )] = depth;
	}
}