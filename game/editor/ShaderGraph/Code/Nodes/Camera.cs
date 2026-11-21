
namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Camera position and shit
/// </summary>
[Title( "Camera" ), Category( "Variables" ), Icon( "photo_camera" )]
public sealed class Camera : ShaderNode
{
	[Output( typeof( Vector3 ) ), Title( "Position" )]
	[Hide]
	public static NodeResult.Func WorldPosition => ( GraphCompiler compiler ) => new( NodeResultType.Vector3, "g_vCameraPositionWs" );

	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func Direction => ( GraphCompiler compiler ) => new( NodeResultType.Vector3, "g_vCameraDirWs" );

	[Output( typeof( float ) )]
	[Hide]
	public static NodeResult.Func NearPlane => ( GraphCompiler compiler ) => new( NodeResultType.Float, "g_flNearPlane" );

	[Output( typeof( float ) )]
	[Hide]
	public static NodeResult.Func FarPlane => ( GraphCompiler compiler ) => new( NodeResultType.Float, "g_flFarPlane" );
}

/// <summary>
/// Sample depth texture
/// </summary>
[Title( "Depth" ), Category( "Camera" )]
public sealed class Depth : ShaderNode
{
	[Input( typeof( Vector2 ) ), Hide]
	public NodeInput UV { get; set; }

	[Output( typeof( float ) ), Hide]
	public NodeResult.Func Out => ( GraphCompiler compiler ) =>
	{
		var result = UV.IsValid() ? compiler.Result( UV ).Cast( 2 ) :
			compiler.IsVs ? "i.vPositionPs.xy" : "i.vPositionSs.xy";

		return new NodeResult( NodeResultType.Float, $"Depth::Get( {result} )" );
	};
}

/// <summary>
/// Sample linear depth, which is absolute coordinates away from the camera
/// </summary>
[Title( "Linear Depth" ), Category( "Camera" )]
public sealed class LinearDepth : ShaderNode
{
	[Input( typeof( Vector2 ) ), Hide]
	public NodeInput UV { get; set; }

	[Output( typeof( float ) ), Hide]
	public NodeResult.Func Out => ( GraphCompiler compiler ) =>
	{
		var result = UV.IsValid() ? compiler.Result( UV ).Cast( 2 ) :
			compiler.IsVs ? "i.vPositionPs.xy" : "i.vPositionSs.xy";

		return new NodeResult( NodeResultType.Float, $"Depth::GetLinear( {result} )" );
	};
}
