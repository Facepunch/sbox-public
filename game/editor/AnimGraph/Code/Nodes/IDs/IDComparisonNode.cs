using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Tests whether the input ID appears in the configured ID list, or inverts that result for <b>Doesn't Match</b>.
/// <br/><br/><b>Use when:</b>&#160;Converting one or more named ID cases into a boolean condition.
/// <br/><b>Important:</b>&#160;<b>Matches</b> with an empty list tests whether the input ID is unset. <b>Doesn't Match</b> with an empty list always returns true. The ID input is required.
/// </summary>
[Title( "ID Comparison" ), Group( "Values" ), Icon( "rule" )]
public class IDComparisonNode : Node
{
	public enum ComparisonType
	{
		Matches,
		[Title( "Doesn't Match" )] DoesntMatch,
	}

	[Input( typeof( IDLink ) )]
	public NodeInput Id { get; set; }

	[Property]
	public ComparisonType Comparison { get; set; } = ComparisonType.Matches;

	[Property]
	public List<string> Ids { get; set; } = [];

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override void BuildDebugWidgets( Widget parent )
	{
		base.BuildDebugWidgets( parent );

		parent.Layout.Add( new Label( Comparison == ComparisonType.Matches ? "One of these:" : "NOT any of these:", parent )
		{
			Color = Theme.TextControl
		} );

		foreach ( var id in Ids ?? [] )
		{
			var label = string.IsNullOrWhiteSpace( id ) ? "INVALID ID" : id;
			parent.Layout.Add( new Label( $"\u2022 {label}", parent ) { Color = Theme.TextControl } );
		}
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var input = compiler.EmitValue( this, Id );
		if ( input is null )
		{
			compiler.Warn( "ID Comparison has no Id connected" );
			return compiler.ConstBool( false );
		}

		return compiler.Builder.IdCompare(
			 input, Ids ?? [], Comparison == ComparisonType.Matches );
	}

}
