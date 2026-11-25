// Borealis Lab's map shader (-map): a colour texture or flat colour, one sun with cascaded shadows and a sky/ground
// ambient. Not s&box's shading: it's there so a map's draws can be issued straight to Borealis.Gpu and timed.
//
// MODE picks where a draw's data comes from, one variant per way of issuing draws (MapRenderer.Issue):
//   0  per draw: one constant buffer with the object and material, the texture and sampler bound beside it (set 1)
//   1  material objects: the material's persistent set (set 1); objects in one buffer indexed by the instance
//   2  bindless: objects and materials in buffers, textures and samplers from the bindless set (multi-draw indirect)

[[vk::binding( 62, 4 )]] Texture2D g_bindless_Texture2D[];
[[vk::binding( 62, 4 )]] Texture2DArray g_bindless_Texture2DArray[];
[[vk::binding( 29, 4 )]] SamplerState g_bindless_Sampler[];
[[vk::binding( 29, 4 )]] SamplerComparisonState g_bindless_SamplerComparison[];

[[vk::constant_id( 0 )]] const uint AlphaTest = 0;

struct View
{
	float4x4 WorldToProjection;
	float4x4 WorldToShadow[4];
	float4 CameraPosition;
	float4 CameraForward;
	float4 SunDirection;
	float4 SunColor;
	float4 SkyColor;
	float4 GroundColor;
	float4 CascadeEnds;
	float4 NormalOffsets;
	uint ShadowMap;
	uint ShadowSampler;
	uint CascadeCount;
	float ShadowTexel;
};

struct Instance
{
	float4 Rows[3];
	float4 Tint;
	uint Material;
	uint3 Pad;
};

struct Material
{
	float4 Color;
	uint Texture;
	uint Sampler;
	float AlphaReference;
	uint Pad;
};

[[vk::binding( 0, 0 )]] ConstantBuffer<View> g_View;

#if MODE == 0
struct Draw
{
	Instance Object;
	Material Surface;
};

[[vk::binding( 0, 1 )]] ConstantBuffer<Draw> g_Draw;
[[vk::binding( 1, 1 )]] Texture2D g_Color;
[[vk::binding( 2, 1 )]] SamplerState g_Sampler;

Instance GetInstance( uint id ) { return g_Draw.Object; }
Material GetMaterial( uint index ) { return g_Draw.Surface; }
float4 SampleColor( Material m, float2 uv ) { return g_Color.Sample( g_Sampler, uv ); }
#elif MODE == 1
[[vk::binding( 1, 0 )]] StructuredBuffer<Instance> g_Instances;
[[vk::binding( 0, 1 )]] ConstantBuffer<Material> g_Material;
[[vk::binding( 1, 1 )]] Texture2D g_Color;
[[vk::binding( 2, 1 )]] SamplerState g_Sampler;

Instance GetInstance( uint id ) { return g_Instances[id]; }
Material GetMaterial( uint index ) { return g_Material; }
float4 SampleColor( Material m, float2 uv ) { return g_Color.Sample( g_Sampler, uv ); }
#else
[[vk::binding( 1, 0 )]] StructuredBuffer<Instance> g_Instances;
[[vk::binding( 2, 0 )]] StructuredBuffer<Material> g_Materials;

Instance GetInstance( uint id ) { return g_Instances[id]; }
Material GetMaterial( uint index ) { return g_Materials[index]; }
float4 SampleColor( Material m, float2 uv ) { return g_bindless_Texture2D[NonUniformResourceIndex( m.Texture )].Sample( g_bindless_Sampler[NonUniformResourceIndex( m.Sampler )], uv ); }
#endif

struct VsInput
{
	[[vk::location( 0 )]] float3 Position : POSITION;
	[[vk::location( 1 )]] float4 Normal : NORMAL;
	[[vk::location( 2 )]] float2 Uv : TEXCOORD0;
	uint InstanceId : SV_InstanceID;
};

struct VsOutput
{
	precise float4 Position : SV_Position;
	float3 World : TEXCOORD1;
	float3 Normal : NORMAL;
	float2 Uv : TEXCOORD0;
	nointerpolation float4 Tint : COLOR0;
	nointerpolation uint Material : TEXCOORD2;
};

// One vertex shader for the depth prepass, shadows and forward, so the prepass and forward depths are identical
VsOutput MainVs( VsInput i )
{
	Instance o = GetInstance( i.InstanceId );
	float4 p = float4( i.Position, 1 );
	precise float3 world = float3( dot( o.Rows[0], p ), dot( o.Rows[1], p ), dot( o.Rows[2], p ) );
	float3 n = i.Normal.xyz;
	float3 normal = float3( dot( o.Rows[0].xyz, n ), dot( o.Rows[1].xyz, n ), dot( o.Rows[2].xyz, n ) );

	VsOutput v;
	v.Position = mul( g_View.WorldToProjection, float4( world, 1 ) );
	v.World = world;
	v.Normal = normal;
	v.Uv = i.Uv;
	v.Tint = o.Tint;
	v.Material = o.Material;
	return v;
}

// Alpha tested depth: the prepass and shadows
void MainDepthPs( VsOutput i )
{
	Material m = GetMaterial( i.Material );
	if ( SampleColor( m, i.Uv ).a * m.Color.a < m.AlphaReference ) discard;
}

float Shadow( float3 world, float3 normal )
{
	float depth = dot( world - g_View.CameraPosition.xyz, g_View.CameraForward.xyz );
	uint cascade = 0;
	[unroll] for ( uint c = 0; c < 3; c++ ) cascade += depth > g_View.CascadeEnds[c] ? 1 : 0;
	if ( cascade >= g_View.CascadeCount ) return 1;

	float4 s = mul( g_View.WorldToShadow[cascade], float4( world + normal * g_View.NormalOffsets[cascade], 1 ) );
	Texture2DArray map = g_bindless_Texture2DArray[g_View.ShadowMap];
	SamplerComparisonState compare = g_bindless_SamplerComparison[g_View.ShadowSampler];

	float lit = 0;
	[unroll] for ( int y = -1; y <= 1; y++ )
	[unroll] for ( int x = -1; x <= 1; x++ )
		lit += map.SampleCmpLevelZero( compare, float3( s.xy + float2( x, y ) * g_View.ShadowTexel, cascade ), s.z );
	return lit / 9;
}

float4 MainPs( VsOutput i ) : SV_Target0
{
	Material m = GetMaterial( i.Material );
	float4 albedo = SampleColor( m, i.Uv ) * m.Color * i.Tint;
	if ( AlphaTest != 0 && albedo.a < m.AlphaReference ) discard;

	float3 n = normalize( i.Normal );
	float sun = saturate( dot( n, g_View.SunDirection.xyz ) );
	float shadow = sun > 0 ? Shadow( i.World, n ) : 0;
	float3 ambient = lerp( g_View.GroundColor.rgb, g_View.SkyColor.rgb, n.z * 0.5 + 0.5 );
	return float4( albedo.rgb * ( ambient + g_View.SunColor.rgb * sun * shadow ), albedo.a );
}
