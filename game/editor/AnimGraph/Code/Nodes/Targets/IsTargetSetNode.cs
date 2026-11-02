using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns whether the connected target currently contains a valid target value.
/// <br/><br/><b>Use when:</b>&#160;Enabling or branching animation behavior only while a runtime target is available.
/// <br/><b>Important:</b>&#160;The target input is required.
/// </summary>
[Title( "Is Target Set" ), Group( "Values" ), Icon( "check_circle" )]
public class IsTargetSetNode : Node
{
	[Input( typeof( TargetLink ) )]
	public NodeInput Target { get; set; }

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var input = compiler.EmitValue( this, Target );
		if ( input is null )
		{
			compiler.Warn( "Is Target Set has no Target connected" );
			return compiler.ConstBool( false );
		}

		return compiler.Builder.IsTargetSet( input );
	}

}
