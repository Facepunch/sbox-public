using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Extracts a target's position as a vector.
/// <br/><br/><b>Use when:</b>&#160;A vector-consuming node needs the positional component of a target.
/// <br/><b>Important:</b>&#160;<b>Is World Space Target</b> controls how the incoming target is interpreted. The target input is required.
/// </summary>
[Title( "Target Point" ), Group( "Values" ), Icon( "place" )]
public class TargetPointNode : Node
{
	[Input( typeof( TargetLink ) )]
	public NodeInput Target { get; set; }

	[Property]
	public bool IsWorldSpaceTarget { get; set; } = true;

	[Output( typeof( VectorLink ) )]
	[JsonIgnore]
	public object Point => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var input = compiler.EmitValue( this, Target );
		if ( input is null )
		{
			compiler.Warn( "Target Point has no Target connected" );
			return compiler.ConstVector( Vector3.Zero );
		}

		return compiler.Builder.TargetPoint( input, IsWorldSpaceTarget );
	}

}
