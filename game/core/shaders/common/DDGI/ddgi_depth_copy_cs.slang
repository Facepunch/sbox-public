// ddgi_depth_copy_cs: a standalone module, the whole shader for Borealis.Gpu (docs/managed/standalone-modules.md). The .shader
// beside it is only Source 2's renderer's.
//---------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Copy depth texture to color texture array slice for DDGI probe rendering";

//---------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//---------------------------------------------------------------------------------------------------------------------

//---------------------------------------------------------------------------------------------------------------------
#include "system.fxc"

//---------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
Texture2D<float> SourceDepth < Attribute( "SourceDepth" ); >;
RWTexture2DArray<float> DestTextureArray < Attribute( "DestTextureArray" ); >;

int2 TextureSize < Attribute( "TextureSize" ); >;
int DestArraySlice < Attribute( "DestArraySlice" ); Default( 0 ); >;

[numthreads( 8, 8, 1 )]
[shader( "compute" )] void MainCs( uint3 vThreadId : SV_DispatchThreadID )
{
	if ( vThreadId.x >= (uint)TextureSize.x || vThreadId.y >= (uint)TextureSize.y )
		return;
	
	float depth = SourceDepth.Load( int3( vThreadId.xy, 0 ) );
	DestTextureArray[int3( vThreadId.xy, DestArraySlice )] = depth;
}

#endif // CS
