// A triangle that discards half its pixels, in a checkerboard (two of each 2x2 quad), and reads derivatives after the
// discard, so the quad's discarded pixels go on as helpers: what a fading object's dither does.

struct VsOutput
{
	float4 Position : SV_Position;
	float3 Color : COLOR0;
};

float4 MainPs( VsOutput i ) : SV_Target0
{
	uint2 pixel = uint2( i.Position.xy );
	if ( ((pixel.x ^ pixel.y) & 1) != 0 ) discard;
	return float4( i.Color + abs( ddx( i.Color ) + ddy( i.Color ) ) * 0.01, 1 );
}
