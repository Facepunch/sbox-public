using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Tests whether the input ID appears in an ID list that can differ per graph variation, or inverts that result for <b>Doesn't Match</b>.
/// <br/><br/><b>Use when:</b>&#160;The set of IDs a condition should match depends on which variation is running.
/// <br/><b>Important:</b>&#160;<b>Matches</b> with an empty list tests whether the input ID is unset. <b>Doesn't Match</b> with an empty list always returns true. The ID input is required.
/// </summary>
[Title( "Variation ID Comparison" ), Group( "Values" ), Icon( "rule" )]
public class VariationIDComparisonNode : VariationDataNode<VariationIDComparisonNode.Data>
{
	/// <summary>
	/// The parts of a variation ID comparison node that can differ per variation.
	/// </summary>
	public sealed class Data : VariationDataNode.VariationData
	{
		[Property]
		public List<string> Ids { get; set; } = [];
	}

	[Input( typeof( IDLink ) )]
	public NodeInput Id { get; set; }

	[Property]
	public IDComparisonNode.ComparisonType Comparison { get; set; } = IDComparisonNode.ComparisonType.Matches;

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#c678dd" ).Value;

	internal override void BuildDebugWidgets( Widget parent )
	{
		base.BuildDebugWidgets( parent );

		parent.Layout.Add( new Label( Comparison == IDComparisonNode.ComparisonType.Matches ? "One of these:" : "NOT any of these:", parent )
		{
			Color = Theme.TextControl
		} );

		foreach ( var id in ((Data)SelectedVariationData).Ids ?? [] )
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
			compiler.Warn( "Variation ID Comparison has no Id connected" );
			return compiler.ConstBool( false );
		}

		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );
		return compiler.Builder.IdCompare(
			input, data.Ids ?? [], Comparison == IDComparisonNode.ComparisonType.Matches );
	}

}
