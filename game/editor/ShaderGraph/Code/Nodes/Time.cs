
namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Current time
/// </summary>
[Title( "Time" ), Category( "Variables" ), Icon( "timer" )]
public sealed class Time : ShaderNode
{
	[JsonIgnore]
	public float Value => RealTime.Now;

	[Output( typeof( float ) ), Title( "Time" )]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return new NodeResult( NodeResultType.Float, compiler.IsPreview ? "g_flPreviewTime" : "g_flTime", compiler.IsNotPreview );
	};
}
