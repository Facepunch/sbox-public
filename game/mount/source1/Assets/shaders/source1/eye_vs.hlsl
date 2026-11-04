#include "common/vertex.hlsl"

PixelInput MainVs( VertexInput v )
{
	return FinalizeVertex( ProcessVertex( v ) );
}
