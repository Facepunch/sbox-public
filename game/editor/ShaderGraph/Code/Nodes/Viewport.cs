namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Info about the current viewport.
/// </summary>
[Title( "Viewport" ), Category( "Variables" ), Icon( "tv" )]
public sealed class ViewportNode : ShaderNode
{
	[Output( typeof( Vector2 ) ), Title( "Size" )]
	[Hide]
	public static NodeResult.Func ViewportSize => ( GraphCompiler compiler ) => compiler.Input( 2, compiler.ViewportSize, "The viewport" );

	[Output( typeof( Vector2 ) ), Title( "Inverse Size" )]
	[Hide]
	public static NodeResult.Func ViewportInverseSize => ( GraphCompiler compiler ) => compiler.Input( 2, compiler.InverseViewportSize, "The viewport" );

	[Output( typeof( Vector2 ) ), Title( "Offset" )]
	[Hide]
	public static NodeResult.Func ViewportOffset => ( GraphCompiler compiler ) => compiler.Input( 2, compiler.ViewportOffset, "The viewport" );

	[Output( typeof( float ) ), Title( "Min Z" )]
	[Hide]
	public static NodeResult.Func ViewportMinZ => ( GraphCompiler compiler ) => compiler.Input( 1, compiler.ViewportMinZ, "The viewport" );

	[Output( typeof( float ) ), Title( "Max Z" )]
	[Hide]
	public static NodeResult.Func ViewportMaxZ => ( GraphCompiler compiler ) => compiler.Input( 1, compiler.ViewportMaxZ, "The viewport" );
}
