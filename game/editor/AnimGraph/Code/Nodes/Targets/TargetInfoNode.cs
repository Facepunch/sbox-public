using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Extracts an angle, distance, or delta-orientation component from a target relative to the character.
/// <br/><br/><b>Use when:</b>&#160;Driving animation parameters from a target's relative direction, distance, or orientation.
/// <br/><b>Important:</b>&#160;<b>Is World Space Target</b> controls how the incoming target is interpreted. The target input is required.
/// </summary>
[Title( "Target Info" ), Group( "Values" ), Icon( "info" )]
public class TargetInfoNode : Node
{
	[Input( typeof( TargetLink ) )]
	public NodeInput Target { get; set; }

	[Property]
	public GraphBuilder.TargetInfoType Info { get; set; } = GraphBuilder.TargetInfoType.Distance;

	[Property]
	public bool IsWorldSpaceTarget { get; set; } = true;

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var input = compiler.EmitValue( this, Target );
		if ( input is null )
		{
			compiler.Warn( "Target Info has no Target connected" );
			return compiler.ConstFloat( 0.0f );
		}

		return compiler.Builder.TargetInfo( input, Info, IsWorldSpaceTarget );
	}

}
