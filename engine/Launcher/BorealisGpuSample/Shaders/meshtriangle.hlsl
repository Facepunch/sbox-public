// triangle.hlsl's triangle drawn by a task and a mesh shader in place of the vertex stage, coloured by triangle.hlsl's
// pixel shader: the same corners, colours and push constants.

struct Constants
{
	float2 Offset;
	float Scale;
	float Pad;
};

[[vk::push_constant]] Constants g_Constants;

struct Payload
{
	float Scale;
};

groupshared Payload s_payload;

[numthreads( 1, 1, 1 )]
void MainTs()
{
	s_payload.Scale = g_Constants.Scale;
	DispatchMesh( 1, 1, 1, s_payload );
}

struct MsOutput
{
	float4 Position : SV_Position;
	float3 Color : COLOR0;
};

[outputtopology( "triangle" )]
[numthreads( 3, 1, 1 )]
void MainMs( uint id : SV_GroupThreadID, in payload Payload payload, out vertices MsOutput vertices[3], out indices uint3 triangles[1] )
{
	static const float2 corners[3] = { float2( 0, 0.6 ), float2( 0.6, -0.5 ), float2( -0.6, -0.5 ) };
	static const float3 colors[3] = { float3( 1, 0.1, 0.1 ), float3( 0.1, 1, 0.1 ), float3( 0.1, 0.2, 1 ) };

	SetMeshOutputCounts( 3, 1 );
	vertices[id].Position = float4( corners[id] * payload.Scale + g_Constants.Offset, 0.5, 1 );
	vertices[id].Color = colors[id];
	if ( id == 0 ) triangles[0] = uint3( 0, 1, 2 );
}
