// A triangle from nothing but its vertex ids, coloured per corner.

struct Constants
{
	float2 Offset;
	float Scale;
	float Pad;
};

[[vk::push_constant]] Constants g_Constants;

struct VsOutput
{
	float4 Position : SV_Position;
	float3 Color : COLOR0;
};

VsOutput MainVs( uint id : SV_VertexID )
{
	static const float2 corners[3] = { float2( 0, 0.6 ), float2( 0.6, -0.5 ), float2( -0.6, -0.5 ) };
	static const float3 colors[3] = { float3( 1, 0.1, 0.1 ), float3( 0.1, 1, 0.1 ), float3( 0.1, 0.2, 1 ) };

	VsOutput o;
	o.Position = float4( corners[id] * g_Constants.Scale + g_Constants.Offset, 0.5, 1 );
	o.Color = colors[id];
	return o;
}

float4 MainPs( VsOutput i ) : SV_Target0
{
	return float4( i.Color, 1 );
}
