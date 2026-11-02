using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns true when either boolean input is true, using short-circuit evaluation.
/// <br/><br/><b>Use when:</b>&#160;Any one of multiple conditions may pass; chain nodes to combine more conditions.
/// <br/><b>Important:</b>&#160;Each disconnected input uses false.
/// </summary>
[Title( "Or" ), Group( "Values" ), Icon( "join_full" )]
public class OrNode : Node
{
	[Input( typeof( BoolLink ) )]
	public NodeInput A { get; set; }

	[Input( typeof( BoolLink ) )]
	public NodeInput B { get; set; }

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var a = compiler.EmitValue( this, A );
		if ( a is null )
			a = compiler.ConstBool( false );
		var b = compiler.EmitValue( this, B );
		if ( b is null )
			b = compiler.ConstBool( false );
		return compiler.Builder.Or( a, b );
	}

}
