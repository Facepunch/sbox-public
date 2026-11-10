using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Starts with the connected vector, or zero when absent, then replaces each component that has a connected float input.
/// <br/><br/><b>Use when:</b>&#160;Assembling a vector from scalar values or overriding selected components of an existing vector.
/// </summary>
[Title( "Vector Create" ), Group( "Values" ), Icon( "merge" )]
public class VectorCreateNode : Node
{
	[Input( typeof( VectorLink ) )]
	public NodeInput Vector { get; set; }

	[Input( typeof( FloatLink ) )]
	public NodeInput X { get; set; }

	[Input( typeof( FloatLink ) )]
	public NodeInput Y { get; set; }

	[Input( typeof( FloatLink ) )]
	public NodeInput Z { get; set; }

	[Output( typeof( VectorLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var vector = compiler.EmitValue( this, Vector );
		var x = compiler.EmitValue( this, X );
		var y = compiler.EmitValue( this, Y );
		var z = compiler.EmitValue( this, Z );

		if ( vector is null && x is null && y is null && z is null )
			return compiler.ConstVector( Vector3.Zero );

		return compiler.Builder.VectorCreate( x, y, z, vector );
	}

}
