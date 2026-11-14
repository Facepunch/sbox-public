// ddgi_depth_copy_cs.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
//---------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Copy depth texture to color texture array slice for DDGI probe rendering";

//---------------------------------------------------------------------------------------------------------------------
// MODES: Default();

#include "sbox/pass.slangh"

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
