
namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Vertex normal in world space
/// </summary>
[Title( "World Normal" ), Category( "Variables" ), Icon( "public" )]
public sealed class WorldNormal : ShaderNode
{
	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func Result => ( GraphCompiler compiler ) => new( 3, compiler.WorldNormal, compiler.IsNotPreview );
}

/// <summary>
/// Vertex tangents in world space
/// </summary>
[Title( "World Tangent" ), Category( "Variables" ), Icon( "public" )]
public sealed class WorldTangent : ShaderNode
{
	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func U => ( GraphCompiler compiler ) => new( 3, compiler.WorldTangentU, compiler.IsNotPreview );

	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func V => ( GraphCompiler compiler ) => new( 3, compiler.WorldTangentV, compiler.IsNotPreview );
}

/// <summary>
/// Whether or not the current pixel is a front-facing pixel.
/// </summary>
[Title( "Is Front Face" ), Category( "Variables" ), Icon( "start" )]
public sealed class IsFrontFace : ShaderNode
{
	[Output( typeof( int ) ), Title( "Result" )]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return new NodeResult( 1, compiler.IsPs ? $"( {compiler.FrontFace} ? 1.0 : 0.0 )" : "0", compiler.IsNotPreview );
	};
}


/// <summary>
/// Vertex normal in object space
/// </summary>
[Title( "Object Space Normal" ), Category( "Variables" ), Icon( "view_in_ar" )]
public sealed class ObjectSpaceNormal : ShaderNode
{
	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func Result => ( GraphCompiler compiler ) => new( 3, compiler.ObjectNormal, compiler.IsNotPreview );
}

/// <summary>
/// Return the current screen position of the object
/// </summary>
[Title( "Screen Position" ), Category( "Variables" ), Icon( "install_desktop" )]
public sealed class ScreenPosition : ShaderNode
{
	// Note: We could make all of these constants but I don't like the situation where it can generated something like
	// "i.vPositionSs.xy.xy" when casting.. even though that should be valid.

	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func XYZ => ( GraphCompiler compiler ) => compiler.Input( 3, compiler.ScreenPosition is { } p ? $"{p}.xyz" : null, "Screen Position" );

	[Output( typeof( Vector2 ) )]
	[Hide]
	public static NodeResult.Func XY => ( GraphCompiler compiler ) => compiler.Input( 2, compiler.ScreenPosition is { } p ? $"{p}.xy" : null, "Screen Position" );

	[Output( typeof( float ) )]
	[Hide]
	public static NodeResult.Func Z => ( GraphCompiler compiler ) => compiler.Input( 1, compiler.ScreenPosition is { } p ? $"{p}.z" : null, "Screen Position" );

	[Output( typeof( float ) )]
	[Hide]
	public static NodeResult.Func W => ( GraphCompiler compiler ) => compiler.Input( 1, compiler.ScreenPosition is { } p ? $"{p}.w" : null, "Screen Position" );
}

/// <summary>
/// Return the current screen uvs of the object
/// </summary>
[Title( "Screen Coordinate" ), Category( "Variables" ), Icon( "tv" )]
public sealed class ScreenCoordinate : ShaderNode
{
	[Output( typeof( Vector2 ) )]
	[Hide]
	public static NodeResult.Func Result => ( GraphCompiler compiler ) =>
		compiler.Input( 2, compiler.ScreenPosition is { } p ? compiler.ViewportUv( $"{p}.xy" ) : null, "Screen Coordinate" );
}

/// <summary>
/// Return the current world space position
/// </summary>
[Title( "World Space Position" ), Category( "Variables" ), Icon( "public" )]
public sealed class WorldPosition : ShaderNode
{
	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func Result => ( GraphCompiler compiler ) =>
		new( 3, compiler.WorldPosition );
}

/// <summary>
/// Return the current object space position of the pixel
/// </summary>
[Title( "Object Space Position" ), Category( "Variables" ), Icon( "view_in_ar" )]
public sealed class ObjectPosition : ShaderNode
{
	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func Result => ( GraphCompiler compiler ) => new( 3, compiler.ObjectPosition );
}

/// <summary>
/// Return the current view direction of the pixel
/// </summary>
[Title( "View Direction" ), Category( "Variables" ), Icon( "cameraswitch" )]
public sealed class ViewDirection : ShaderNode
{
	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func Result => ( GraphCompiler compiler ) =>
		new( 3, compiler.PositionToCamera( compiler.WorldPosition ) );
}

/// <summary>
/// Color of the vertex
/// </summary>
[Title( "Vertex Color" ), Category( "Variables" ), Icon( "format_color_fill" )]
public sealed class VertexColor : ShaderNode
{
	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func RGB => ( GraphCompiler compiler ) => new( 3, $"{compiler.VertexColor}.rgb" );

	[Output( typeof( float ) )]
	[Hide]
	public static NodeResult.Func Alpha => ( GraphCompiler compiler ) => new( 1, $"{compiler.VertexColor}.a" );
}

/// <summary>
/// Tint of the scene object
/// </summary>
[Title( "Tint" ), Category( "Variables" ), Icon( "palette" )]
public sealed class Tint : ShaderNode
{
	[Hide, Output( typeof( Color ) )]
	public static NodeResult.Func RGBA => ( GraphCompiler compiler ) => new( 4, compiler.Tint );
}
