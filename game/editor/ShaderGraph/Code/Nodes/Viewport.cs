namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Info about the current viewport.
/// </summary>
[Title( "Viewport" ), Category( "Variables" ), Icon( "tv" )]
public sealed class ViewportNode : ShaderNode
{
	[Output( typeof( Vector2 ) ), Title( "Size" )]
	[Hide]
	public static NodeResult.Func ViewportSize => ( GraphCompiler compiler ) => new( 2, "g_vViewportSize" );

	[Output( typeof( Vector2 ) ), Title( "Inverse Size" )]
	[Hide]
	public static NodeResult.Func ViewportInverseSize => ( GraphCompiler compiler ) => new( 2, "g_vInvViewportSize" );

	[Output( typeof( Vector2 ) ), Title( "Offset" )]
	[Hide]
	public static NodeResult.Func ViewportOffset => ( GraphCompiler compiler ) => new( 2, "g_vViewportOffset" );

	[Output( typeof( float ) ), Title( "Min Z" )]
	[Hide]
	public static NodeResult.Func ViewportMinZ => ( GraphCompiler compiler ) => new( 1, "g_flViewportMinZ" );

	[Output( typeof( float ) ), Title( "Max Z" )]
	[Hide]
	public static NodeResult.Func ViewportMaxZ => ( GraphCompiler compiler ) => new( 1, "g_flViewportMaxZ" );
}
