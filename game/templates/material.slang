// A material shader: a Slang module that describes its whole shader. Name it in a material as shaders/<this file's name>.shader.
#include "sbox/pass.slangh"

Mode( Forward );
Mode( Depth );

#include "common/features.hlsl"
#include "common/shared.hlsl"

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

#if ( PROGRAM == VFX_PROGRAM_VS )
	#include "common/vertex.hlsl"

	[shader( "vertex" )] PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		// Add your vertex manipulation functions here
		return FinalizeVertex( o );
	}
#endif

#if ( PROGRAM == VFX_PROGRAM_PS )
	#include "common/pixel.hlsl"

	[shader( "pixel" )] float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );
		/* m.Metalness = 1.0f; // Forces the object to be metalic */
		return ShadingModelStandard::Shade( m );
	}
#endif
