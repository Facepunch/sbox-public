using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns true while a graph instance is connected to a named External Graph slot.
/// <br/><br/><b>Use when:</b>&#160;Selecting behavior based on whether optional external animation logic is attached.
/// <br/><b>Important:</b>&#160;If no matching External Graph node exists, the node warns and returns false.
/// </summary>
[Title( "Is External Slot Filled" ), Group( "Values" ), Icon( "check_circle" )]
public class IsExternalGraphSlotFilledNode : Node
{
	/// <summary> Slot name of the External Graph node to check. </summary>
	[Property]
	public string SlotName { get; set; } = "";

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var slotNode = Root?.AllNodes.OfType<ExternalReferencedGraphNode>().FirstOrDefault( n => n.SlotName == SlotName );
		if ( slotNode is null )
		{
			compiler.Warn( $"No External Graph node with slot name '{SlotName}'", this );
			return compiler.ConstBool( false );
		}

		var slot = compiler.Emit( slotNode );
		return compiler.Builder.IsExternalGraphSlotFilled( slot );
	}

}
