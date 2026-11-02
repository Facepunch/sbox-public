using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Applies per-bone scaling to a pose using a bone mask, or applies <b>Default Scale</b> uniformly when no mask is connected.
/// <br/><br/><b>Use when:</b>&#160;Scaling selected bones or an entire animated pose.
/// <br/><b>Important:</b>&#160;<b>Enable</b> is optional - leave it disconnected to always scale.
/// </summary>
[Title( "Scale" ), Group( "Blend" ), Icon( "tune" )]
public class ScaleNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Source { get; set; }

	[Input( typeof( BoneMaskLink ) )]
	public NodeInput BoneMask { get; set; }

	[Input( typeof( BoolLink ) )]
	public NodeInput Enable { get; set; }

	[Property]
	public float DefaultScale { get; set; } = 1.0f;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Source, "Scale" );
		var mask = compiler.EmitValue( this, BoneMask ) ?? compiler.FixedWeightBoneMask( DefaultScale );
		return compiler.Builder.Scale( child, mask, compiler.EmitValue( this, Enable ) );
	}

}
