using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Negates all three components of the input vector.
/// <br/><br/><b>Use when:</b>&#160;Reversing a direction or offset.
/// <br/><b>Important:</b>&#160;The vector input is required.
/// </summary>
[Title( "Vector Negate" ), Group( "Values" ), Icon( "invert_colors" )]
public class VectorNegateNode : Node
{
	[Input( typeof( VectorLink ) )]
	public NodeInput Vector { get; set; }

	[Output( typeof( VectorLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var input = compiler.EmitValue( this, Vector );
		if ( input is null )
		{
			compiler.Warn( "Vector Negate has no Vector connected" );
			return compiler.ConstVector( Vector3.Zero );
		}

		return compiler.Builder.VectorNegate( input );
	}

}
