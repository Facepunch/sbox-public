// An unlit shader: a Slang module that describes its whole shader. Name it in a material as shaders/<this file's name>.shader.
#include "sbox/pass.slangh"

Mode( Forward );
Mode( Depth );

#include "sbox/common_features.slangh"
#include "sbox/shared.slangh"

struct VertexInput
{
	#include "sbox/common_vertex_input.slangh"
};

struct PixelInput
{
	#include "sbox/common_pixel_input.slangh"
};

#if ( PROGRAM == VFX_PROGRAM_VS )
	#include "sbox/vertex.slangh"

	[shader( "vertex" )] PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		// Add your vertex manipulation functions here
		return FinalizeVertex( o );
	}
#endif

#if ( PROGRAM == VFX_PROGRAM_PS )
	#include "sbox/pixel.slangh"

	[shader( "pixel" )] float4 MainPs( PixelInput i ) : SV_Target0
	{
		return float4( 1, 1, 1, 1 );
	}
#endif
