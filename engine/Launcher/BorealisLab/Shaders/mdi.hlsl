// Borealis Lab's multi-draw benchmark shader (-mdi-bench): positions already in clip space, depth only.

float4 MainVs( float4 vPosition : POSITION ) : SV_Position
{
	return vPosition;
}
