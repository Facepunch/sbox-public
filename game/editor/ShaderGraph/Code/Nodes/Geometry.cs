
namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Vertex normal in world space
/// </summary>
[Title( "World Normal" ), Category( "Variables" ), Icon( "public" )]
public sealed class WorldNormal : ShaderNode
{
	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func Result => ( GraphCompiler compiler ) => new( NodeResultType.Vector3, "i.vNormalWs", compiler.IsNotPreview );
}

/// <summary>
/// Vertex tangents in world space
/// </summary>
[Title( "World Tangent" ), Category( "Variables" ), Icon( "public" )]
public sealed class WorldTangent : ShaderNode
{
	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func U => ( GraphCompiler compiler ) => new( NodeResultType.Vector3, "i.vTangentUWs", compiler.IsNotPreview );

	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func V => ( GraphCompiler compiler ) => new( NodeResultType.Vector3, "i.vTangentVWs", compiler.IsNotPreview );
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
		return new NodeResult( NodeResultType.Float, compiler.IsPs ? "i.vFrontFacing" : "0", compiler.IsNotPreview );
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
	public static NodeResult.Func Result => ( GraphCompiler compiler ) => new( NodeResultType.Vector3, "i.vNormalOs", compiler.IsNotPreview );
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
	public static NodeResult.Func XYZ => ( GraphCompiler compiler ) => compiler.IsVs ? new( NodeResultType.Vector3, "i.vPositionPs.xyz" ) : new( NodeResultType.Vector3, "i.vPositionSs.xyz" );

	[Output( typeof( Vector2 ) )]
	[Hide]
	public static NodeResult.Func XY => ( GraphCompiler compiler ) => compiler.IsVs ? new( NodeResultType.Vector2, "i.vPositionPs.xy" ) : new( NodeResultType.Vector2, "i.vPositionSs.xy" );

	[Output( typeof( float ) )]
	[Hide]
	public static NodeResult.Func Z => ( GraphCompiler compiler ) => compiler.IsVs ? new( NodeResultType.Float, "i.vPositionPs.z" ) : new( NodeResultType.Float, "i.vPositionSs.z" );

	[Output( typeof( float ) )]
	[Hide]
	public static NodeResult.Func W => ( GraphCompiler compiler ) => compiler.IsVs ? new( NodeResultType.Float, "i.vPositionPs.w" ) : new( NodeResultType.Float, "i.vPositionSs.w" );
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
		compiler.IsVs ?
		new( NodeResultType.Vector2, "CalculateViewportUv( i.vPositionPs.xy )" ) :
		new( NodeResultType.Vector2, "CalculateViewportUv( i.vPositionSs.xy )" );
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
		compiler.IsVs ?
		new( NodeResultType.Vector3, "i.vPositionWs" ) :
		new( NodeResultType.Vector3, "i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz" );
}

/// <summary>
/// Return the current object space position of the pixel
/// </summary>
[Title( "Object Space Position" ), Category( "Variables" ), Icon( "view_in_ar" )]
public sealed class ObjectPosition : ShaderNode
{
	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func Result => ( GraphCompiler compiler ) => new( NodeResultType.Vector3, "i.vPositionOs" );
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
		compiler.IsVs ?
		new( NodeResultType.Vector3, "CalculatePositionToCameraDirWs( i.vPositionWs )" ) :
		new( NodeResultType.Vector3, "CalculatePositionToCameraDirWs( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz )" );
}

/// <summary>
/// Color of the vertex
/// </summary>
[Title( "Vertex Color" ), Category( "Variables" ), Icon( "format_color_fill" )]
public sealed class VertexColor : ShaderNode
{
	[Output( typeof( Vector3 ) )]
	[Hide]
	public static NodeResult.Func RGB => ( GraphCompiler compiler ) => new( NodeResultType.Vector3, "i.vColor.rgb" );

	[Output( typeof( float ) )]
	[Hide]
	public static NodeResult.Func Alpha => ( GraphCompiler compiler ) => new( NodeResultType.Float, "i.vColor.a" );
}

/// <summary>
/// Tint of the scene object
/// </summary>
[Title( "Tint" ), Category( "Variables" ), Icon( "palette" )]
public sealed class Tint : ShaderNode
{
	[Hide, Output( typeof( Vector4 ) )]
	public static NodeResult.Func RGBA => ( GraphCompiler compiler ) => new( NodeResultType.Vector4, "i.vTintColor" );
}
