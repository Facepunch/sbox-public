namespace Editor.AnimGraph;

/// <summary>
/// Marks a widget as the full-page editor for a node type, opened by
/// double-clicking the node in the graph. That's all it takes to add a page -
/// give the widget a <c>( Widget parent, TNode node, Window window )</c>
/// constructor and implement <see cref="INodePage"/>.
/// </summary>
[AttributeUsage( AttributeTargets.Class )]
internal sealed class NodePageAttribute( Type nodeType ) : Attribute
{
	public Type NodeType { get; } = nodeType;
}

/// <summary>
/// Implemented by node page widgets so undo can swap the graph out from under
/// them without recreating the widget - keeps view position and selection,
/// no flicker.
/// </summary>
internal interface INodePage
{
	/// <summary>
	/// Point the page at its node in a rebuilt graph. Return false if it isn't
	/// the same node - the page gets recreated instead.
	/// </summary>
	bool Rebind( Node node );

	/// <summary>
	/// The canvas whose view gets remembered when navigating away.
	/// </summary>
	CanvasWidget ViewCanvas => this as CanvasWidget;

	/// <summary>
	/// The graph view currently receiving editing shortcuts.
	/// </summary>
	View FocusedGraphView => null;

	/// <summary>
	/// A graph view hosted inside this page.
	/// </summary>
	View EmbeddedGraphView => null;

	bool CanCutSelection => false;
	bool CanCopySelection => false;
	bool CanPasteSelection => false;
	bool CutSelection() => false;
	bool CopySelection() => false;
	bool PasteSelection() => false;

	/// <summary>
	/// Whether selections on this page can expose a secondary graph.
	/// </summary>
	bool SupportsSecondaryView => false;

	void OnGraphEdited() { }
}
