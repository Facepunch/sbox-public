#include "common/vertex.hlsl"
#include "source1/alpha.hlsl"
#include "source1/frames.hlsl"

PixelInput MainVs( VertexInput v )
{
	PixelInput i = ProcessVertex( v );
	i.vSourceColor = v.vSourceColor;
	i.flSourceAlpha = SourceAlpha();
	i.flSourceFrame = SourceFrame();
	i.vSourceOriginWs = mul( GetTransformMatrix( v.nInstanceTransformID ), float4( 0, 0, 0, 1 ) ).xyz;
	return FinalizeVertex( i );
}
