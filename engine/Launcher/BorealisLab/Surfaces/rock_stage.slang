// rock.slang with a vertex stage of its own (IVertexStageSurface): the engine's transform written out, the uv passed on as
// a Varying and read with its derivatives, and a clip plane at ClipHeight (below everything by default). Drawn against
// rock.slang, it must draw the same; with ClipHeight raised, only what's above it.
import sbox.surface;

public struct RockStageVertex
{
	float3 Position : POSITION;
	float3 Normal : NORMAL;
	float2 Uv : TEXCOORD0;
	float4 Tangent : TANGENT;
}

public struct RockStageVaryings
{
	float2 Uv;
	nointerpolation uint Instance;
}

[Material, ClipDistances( 1 )]
public struct RockStage : IVertexStageSurface
{
	typealias VertexInput = RockStageVertex;
	typealias Varyings = RockStageVaryings;

	[Group( "Color" ), Srgb, Opacity] Texture2D Color;
	[Group( "Normal" )] Texture2D Normal;
	[Group( "Roughness" )] Texture2D Rma;
	[Group( "Color" ), Default3( 1, 1, 1 )] float3 Tint;
	[Group( "Roughness" ), Range( 0, 2 ), Default( 1 )] float RoughnessScale;
	[Group( "Clip" ), Default( -100000 )] float ClipHeight;

	public SurfaceVertex Vertex( RockStageVertex v, VertexStageInput s, out RockStageVaryings varyings )
	{
		SurfaceVertex o = {};
		o.Position = mul( s.ObjectToWorld, float4( v.Position, 1 ) );
		o.Normal = normalize( mul( (float3x3)s.ObjectToWorld, v.Normal ) );
		float3 u = mul( (float3x3)s.ObjectToWorld, v.Tangent.xyz );
		o.TangentU = normalize( u - o.Normal * dot( u, o.Normal ) );
		o.TangentV = cross( o.Normal, o.TangentU ) * v.Tangent.w;
		o.Uv = v.Uv;
		o.ClipDistance.x = o.Position.z - ClipHeight;

		varyings.Uv = v.Uv;
		varyings.Instance = s.InstanceId;
		return o;
	}

	public Material Evaluate( SurfaceInput i, RockStageVaryings v, Derivatives<RockStageVaryings> d )
	{
		// The colour with the Varying's own derivatives, the rest as rock.slang reads them
		float4 color = Color.SampleGrad( g_MaterialFiltering, v.Uv, d.Ddx.Uv, d.Ddy.Uv );
		float3 rma = i.Sample( Rma, i.Uv ).rgb;
		float2 n = i.Sample( Normal, i.Uv ).xy * 2 - 1;

		Material m = {};
		m.Albedo = color.rgb * Tint * i.Color.rgb * ( v.Instance == 0 ? 1.0 : 0.5 );
		m.Opacity = color.a;
		m.Roughness = saturate( rma.r * RoughnessScale );
		m.Metalness = rma.g;
		m.AmbientOcclusion = rma.b;
		m.Normal = i.ToWorld( float3( n, sqrt( saturate( 1 - dot( n, n ) ) ) ) );
		return m;
	}
}
