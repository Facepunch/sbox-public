using Editor.NodeEditor;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Displays a resizable, colored comment box for organizing and documenting the graph.
/// <br/><br/><b>Use when:</b>&#160;Grouping, labeling, or explaining related animation logic for graph authors.
/// <br/><b>Important:</b>&#160;Comment nodes are editor-only and never affect the runtime animation graph.
/// </summary>
[Icon( "notes" ), Hide]
public sealed class CommentNode : Node, ICommentNode
{
	[Hide, Browsable( false )]
	public Vector2 Size { get; set; }

	[Hide, Browsable( false )]
	public override string Title { get; set; } = "Untitled";

	[Property, TextArea]
	public string Description { get; set; } = "";

	[Property]
	public CommentColor Color { get; set; } = CommentColor.Green;

	[Hide, Browsable( false )]
	public int Layer { get; set; } = 5;

	[JsonIgnore, Hide, Browsable( false )]
	protected override DisplayInfo DefaultDisplayInfo
	{
		get
		{
			var info = TypeDisplayInfo;

			info.Name = "Untitled";
			info.Description = Description;

			return info;
		}
	}

	public override NodeUI CreateUI( GraphView view ) => new CommentUI( view, this );
}
