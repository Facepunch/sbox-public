using Editor.NodeEditor;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Runs another AnimGraph asset as a child of this graph, copying parent control parameters to child parameters with matching names every update.
/// <br/><br/><b>Use when:</b>&#160;Reusing a fixed AnimGraph asset as a subgraph.
/// <br/><b>Important:</b>&#160;The child instance resets when this node initializes. If the asset cannot be supplied, the fallback pose is used; without a fallback the node is invalid.
/// </summary>
[Title( "Internal Graph" ), Group( "Graphs" ), Icon( "account_tree" )]
public class InternalReferencedGraphNode : VariationDataNode<InternalReferencedGraphNode.Data>
{
	/// <summary>
	/// The parts of an internal graph node that can differ per variation.
	/// </summary>
	public sealed class Data : VariationDataNode.VariationData
	{
		/// <summary> Path of the animgraph asset to run. </summary>
		[Property, ResourceType( "ag" ), JsonIgnore]
		public string GraphPath
		{
			get => GraphReference.ResolvePath() ?? "";
			set => GraphReference = ResourceId.FromPath( value );
		}

		[Hide, JsonPropertyName( "GraphPath" )]
		public ResourceId GraphReference { get; set; }

		/// <summary> Variation of that asset to run. Blank runs its default variation. </summary>
		[Property]
		public string GraphVariation { get; set; } = "";

		public override VisualState GetVisualState()
		{
			return string.IsNullOrWhiteSpace( GraphPath ) ? VisualState.HasUnsetData : VisualState.None;
		}
	}

	/// <summary> Path of the animgraph asset to run, for the variation being edited. </summary>
	[Hide, JsonIgnore]
	public string GraphPath
	{
		get => SelectedData.GraphPath;
		set => SelectedData.GraphPath = value;
	}

	/// <summary> Pose used when the child graph can't be loaded. </summary>
	[Input( typeof( PoseLink ) )]
	public NodeInput Fallback { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#9575cd" ).Value;

	public override Menu CreateContextMenu( NodeUI node )
	{
		if ( node.Graph is not IInternalGraphEditorActions editor )
			return null;

		var menu = new Menu( "Internal Graph" );
		var option = menu.AddOption( "Reflect Child Parameters to Parent", "arrow_back", () => editor.ReflectChildParametersToParent( this ) );
		option.Enabled = !string.IsNullOrWhiteSpace( GraphPath );
		return menu;
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var fallback = compiler.EmitValue( this, Fallback );
		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );

		if ( string.IsNullOrWhiteSpace( data.GraphPath ) )
		{
			compiler.Warn( "Sub-Graph has no graph asset assigned", this );
			return fallback ?? compiler.ReferencePose();
		}

		var variation = string.IsNullOrWhiteSpace( data.GraphVariation ) || data.GraphVariation == Variation.DefaultVariationID ? "" : data.GraphVariation;
		return compiler.Builder.ReferencedGraph( data.GraphReference, fallback, variation );
	}

}
