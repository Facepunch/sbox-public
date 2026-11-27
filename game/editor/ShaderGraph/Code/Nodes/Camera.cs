
namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Camera position and shit
/// </summary>
[Title( "Camera" ), Category( "Variables" ), Icon( "photo_camera" )]
public sealed class Camera : ShaderNode
{
	[Output( typeof( Vector3 ) ), Title( "Position" )]
	[Hide]
	public static NodeResult.Func WorldPosition => ( GraphCompiler compiler ) => new( 3, compiler.CameraPosition );

	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func Direction => ( GraphCompiler compiler ) => compiler.Input( 3, compiler.CameraDirection, "The camera's direction" );

	[Output( typeof( float ) )]
	[Hide]
	public static NodeResult.Func NearPlane => ( GraphCompiler compiler ) => compiler.Input( 1, compiler.NearPlane, "The near plane" );

	[Output( typeof( float ) )]
	[Hide]
	public static NodeResult.Func FarPlane => ( GraphCompiler compiler ) => compiler.Input( 1, compiler.FarPlane, "The far plane" );
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
		var result = UV.IsValid() ? compiler.Result( UV ).Cast( 2 ) : compiler.ScreenPosition is { } p ? $"{p}.xy" : null;

		return compiler.Input( 1, result is null ? null : compiler.Depth( result ), "The depth" );
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
		var result = UV.IsValid() ? compiler.Result( UV ).Cast( 2 ) : compiler.ScreenPosition is { } p ? $"{p}.xy" : null;

		return compiler.Input( 1, result is null ? null : compiler.LinearDepth( result ), "The linear depth" );
	};
}
