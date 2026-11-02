using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs the logical inverse of its boolean input.
/// <br/><br/><b>Use when:</b>&#160;A condition should pass only when another condition is false.
/// <br/><b>Important:</b>&#160;A disconnected input uses false, so the result is true.
/// </summary>
[Title( "Not" ), Group( "Values" ), Icon( "block" )]
public class NotNode : Node
{
	[Input( typeof( BoolLink ) )]
	public NodeInput Value { get; set; }

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var value = compiler.EmitValue( this, Value );
		if ( value is null )
			value = compiler.ConstBool( false );
		return compiler.Builder.Not( value );
	}

}
