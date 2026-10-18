namespace Sandbox.UI.NodeGraph;

/// <summary>
/// A graph shown by a <see cref="NodeGraphPanel"/>.
/// </summary>
public interface IGraph
{
	IEnumerable<INode> Nodes { get; }

	void AddNode( INode node );
	void RemoveNode( INode node );
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
