
namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Direction and color of the main directional light (sun). Direction points the way the light
/// travels, negate it to get the direction towards the sun.
/// </summary>
[Title( "Sun" ), Category( "Variables" ), Icon( "light_mode" )]
public sealed class Sun : ShaderNode
{
	[Output( typeof( Vector3 ) ), Title( "Direction" )]
	[Hide]
	public static NodeResult.Func Direction => ( GraphCompiler compiler ) => new( 3, "g_DirectionalLightDirection.xyz" );

	[Output( typeof( Vector3 ) ), Title( "Color" )]
	[Hide]
	public static NodeResult.Func Color => ( GraphCompiler compiler ) => new( 3, "g_DirectionalLightColor.rgb" );
}
