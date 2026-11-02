using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs the true or false ID according to <b>Condition</b>. Each branch uses its connected ID input when present, otherwise its configured constant value.
/// <br/><br/><b>Use when:</b>&#160;Choosing between two IDs from a boolean state.
/// <br/><b>Important:</b>&#160;A disconnected condition evaluates as false.
/// </summary>
[Title( "ID Switch" ), Group( "Values" ), Icon( "alt_route" )]
public class IDSwitchNode : Node
{
	[Input( typeof( BoolLink ) )]
	public NodeInput Condition { get; set; }

	[Input( typeof( IDLink ) )]
	public NodeInput IfTrue { get; set; }

	[Input( typeof( IDLink ) )]
	public NodeInput IfFalse { get; set; }

	[Property]
	public string TrueValue { get; set; } = "";

	[Property]
	public string FalseValue { get; set; } = "";

	[Output( typeof( IDLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		return compiler.Builder.IdSwitch(
			compiler.EmitValue( this, Condition ),
			new GraphBuilder.IdSwitchSettings
			{
				WhenTrue = compiler.EmitValue( this, IfTrue ),
				TrueValue = TrueValue,
				WhenFalse = compiler.EmitValue( this, IfFalse ),
				FalseValue = FalseValue,
			} );
	}

}
