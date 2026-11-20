// A compute shader: a Slang module that describes its whole shader. Run it with new ComputeShader( "shaders/<this file's name>.shader" ).
#include "sbox/pass.slangh"

Mode( Default );

#include "sbox/system.slangh"

#if ( PROGRAM == VFX_PROGRAM_CS )
	RWTexture2D<float4> Result < Attribute( "Result" ); >;

	[numthreads( 8, 8, 1 )]
	[shader( "compute" )] void MainCs( uint3 id : SV_DispatchThreadID )
	{
		Result[ id.xy ] = float4( id.x & id.y, ( id.x & 15 ) / 15.0, ( id.y & 15 ) / 15.0, 0.0 );
	}
#endif
