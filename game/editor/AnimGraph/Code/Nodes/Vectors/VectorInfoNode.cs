using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Extracts a component, length, horizontal angle, or vertical angle from a vector. Angles are returned in degrees.
/// <br/><br/><b>Use when:</b>&#160;Deriving a scalar component, magnitude, yaw-like direction, or elevation from a vector.
/// <br/><b>Important:</b>&#160;The vector input is required.
/// </summary>
[Title( "Vector Info" ), Group( "Values" ), Icon( "info" )]
public class VectorInfoNode : Node
{
	[Input( typeof( VectorLink ) )]
	public NodeInput Vector { get; set; }

	[Property]
	public GraphBuilder.VectorComponent Info { get; set; } = GraphBuilder.VectorComponent.X;

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var input = compiler.EmitValue( this, Vector );
		if ( input is null )
		{
			compiler.Warn( "Vector Info has no Vector connected" );
			return compiler.ConstFloat( 0.0f );
		}

		return compiler.Builder.VectorInfo( input, Info );
	}

}
