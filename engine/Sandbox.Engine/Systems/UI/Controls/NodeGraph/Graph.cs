namespace Sandbox.UI.NodeGraph;

/// <summary>
/// A graph shown by a <see cref="NodeGraphPanel"/>.
/// </summary>
public interface IGraph
{
	IEnumerable<INode> Nodes { get; }

	void AddNode( INode node );
	void RemoveNode( INode node );

	/// <summary>
	/// <paramref name="nodes"/> as text, for copy and paste. Null when this graph can't copy nodes.
	/// </summary>
	string SerializeNodes( IEnumerable<INode> nodes ) => null;

	/// <summary>
	/// Create the nodes in <paramref name="serialized"/>, from <see cref="SerializeNodes"/>, add them
	/// to the graph and return them. Give them new identifiers where they clash with nodes already
	/// here, and keep the connections between them.
	/// </summary>
	IEnumerable<INode> DeserializeNodes( string serialized ) => [];
}

/// <summary>
/// A node shown by a <see cref="NodePanel"/>. Sizes and positions are in canvas units.
/// </summary>
public interface INode
{
	/// <summary>
	/// Raise when plugs, colours or anything else drawn changes.
	/// </summary>
	event Action Changed;

	string Identifier { get; }
	DisplayInfo DisplayInfo { get; }

	/// <summary>
	/// Shown in brackets under the title, when <see cref="HasSubtitle"/>.
	/// </summary>
	string Subtitle => null;

	bool CanRemove => true;

	Vector2 Position { get; set; }
	Vector2 ExpandSize => default;

	bool AutoSize { get; }

	IEnumerable<IPlugIn> Inputs { get; }
	IEnumerable<IPlugOut> Outputs { get; }

	string ErrorMessage { get; }
	bool IsReachable { get; }

	Texture Thumbnail => null;

	bool HasTitleBar => true;
	bool HasSubtitle => false;

	/// <summary>
	/// Tint of the body, plug labels and title text.
	/// </summary>
	Color PrimaryColor { get; }

	/// <summary>
	/// Colour of the title bar.
	/// </summary>
	Color TitleColor => PrimaryColor;

	/// <summary>
	/// Draw over the node after its body and title. <paramref name="rect"/> is the whole node, in node units.
	/// </summary>
	void OnPaint<T>( T paint, Rect rect ) where T : INodePaint, allows ref struct { }
}

/// <summary>
/// A comment: a titled box drawn behind the nodes it holds. Its members move with it and it grows
/// to keep them inside; its edges resize it. Nodes join by being dropped inside it and leave through
/// its right-click menu.
/// </summary>
public interface ICommentNode : INode
{
	string Title { get; }
	string Description => null;
	Color Color { get; }

	/// <summary>
	/// In points, like the editor's comments.
	/// </summary>
	int DescriptionFontSize => 11;

	/// <summary>
	/// In canvas units. Set by resizing; written back when the comment grows or fits to its members.
	/// </summary>
	Vector2 Size { get; set; }

	/// <summary>
	/// Identifiers of the nodes this comment holds.
	/// </summary>
	List<string> Members { get; }
}

/// <summary>
/// An input or output of an <see cref="INode"/>.
/// </summary>
public interface IPlug
{
	INode Node { get; }
	string Identifier { get; }
	Type Type { get; }
	DisplayInfo DisplayInfo { get; }

	/// <summary>
	/// The inline editor for this plug's value, or null for none.
	/// </summary>
	NodeValueEditor CreateEditor( PlugPanel plug ) => null;

	bool ShowLabel { get; }
	bool AllowStretch { get; }
	bool ShowConnection { get; }
	bool InTitleBar { get; }
	bool IsReachable { get; }

	string ErrorMessage { get; }
}

public interface IPlugIn : IPlug
{
	IPlugOut ConnectedOutput { get; set; }
	float? GetHandleOffset( string name );
	void SetHandleOffset( string name, float? value );
}

public interface IPlugOut : IPlug
{
}

public enum HandleShape
{
	Square,
	Circle,
	Arrow
}

/// <summary>
/// How plugs and wires of a value type look.
/// </summary>
public record struct NodeHandleConfig( string Name, Color Color, HandleShape Shape = HandleShape.Square );
