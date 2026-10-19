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

	/// <summary>
	/// Double-clicked. Call <see cref="PanelEvent.StopPropagation"/> on <paramref name="e"/> to take
	/// it, else the graph's default runs.
	/// </summary>
	void OnDoubleClick( NodePanel panel, MousePanelEvent e ) { }

	/// <summary>
	/// Extra right-click entries, shown as a submenu of the graph's menu: set its
	/// <see cref="Menu.Text"/>. Null for none.
	/// </summary>
	Menu CreateContextMenu( NodePanel panel ) => null;

	/// <summary>
	/// Shown as "Go to Definition" in the right-click menu when not null.
	/// </summary>
	Action GoToDefinition => null;
}

/// <summary>
/// A reroute: a dot with one input and one output that bends a wire.
/// </summary>
public interface IRerouteNode : INode
{
	/// <summary>
	/// Shown in a bubble above the dot when not blank.
	/// </summary>
	string Comment { get; set; }
}

/// <summary>
/// A comment: a titled box drawn behind the nodes it holds. Its members move with it and it grows
/// to keep them inside; its edges resize it. Nodes join by being dropped inside it and leave through
/// its right-click menu.
/// </summary>
public interface ICommentNode : INode
{
	string Title { get; set; }
	string Description => null;
	Color Color { get; set; }

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

	/// <summary>
	/// Right-click menu for this plug, or null to leave the click to the node.
	/// </summary>
	Menu CreateContextMenu( PlugPanel plug ) => null;

	/// <summary>
	/// Double-clicked. Call <see cref="PanelEvent.StopPropagation"/> on <paramref name="e"/> to take
	/// it, else the node gets it.
	/// </summary>
	void OnDoubleClick( PlugPanel plug, MousePanelEvent e ) { }

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

/// <summary>
/// One level of a node type's menu path, like the editor's <c>Menu.PathElement</c>.
/// </summary>
public record struct NodePathElement( string Name, string Icon = null, string Description = null, int? Order = null, bool IsHeading = false ) : IComparable<NodePathElement>
{
	public int CompareTo( NodePathElement other )
	{
		var orderComparison = Nullable.Compare( Order, other.Order );
		if ( orderComparison != 0 )
		{
			return orderComparison;
		}

		return string.Compare( Name, other.Name, StringComparison.Ordinal );
	}

	/// <summary>
	/// Menu order: level by level, entries before headings, then by order, then by name. A path
	/// that runs out first sorts last.
	/// </summary>
	public static int Compare( IReadOnlyList<NodePathElement> a, IReadOnlyList<NodePathElement> b )
	{
		for ( var i = 0; ; ++i )
		{
			if ( a.Count == i && b.Count == i )
			{
				return 0;
			}

			if ( a.Count == i )
			{
				return 1;
			}

			if ( b.Count == i )
			{
				return -1;
			}

			var aHead = a[i];
			var bHead = b[i];

			var isHeadingCompare = aHead.IsHeading.CompareTo( bHead.IsHeading );
			if ( isHeadingCompare != 0 ) return isHeadingCompare;

			var orderCompare = (aHead.Order ?? 0).CompareTo( bHead.Order ?? 0 );
			if ( orderCompare != 0 ) return orderCompare;

			var nameCompare = string.Compare( aHead.Name, bHead.Name, StringComparison.Ordinal );
			if ( nameCompare != 0 ) return nameCompare;
		}
	}

	public bool Matches( NodePathElement other )
	{
		return Name == other.Name && IsHeading == other.IsHeading;
	}

	public NodePathElement Merge( NodePathElement other )
	{
		return this with { Icon = Icon ?? other.Icon, Description = Description ?? other.Description, Order = Order ?? other.Order };
	}
}

/// <summary>
/// A kind of node the "Create Node" menu offers.
/// </summary>
public interface INodeType
{
	public static bool DefaultMatches( INodeType nodeType, NodeQuery query )
	{
		return DefaultGetScore( nodeType, query ) != null;
	}

	/// <summary>
	/// <paramref name="query"/>'s score for the type's path, or null when it doesn't match: the
	/// filter misses, the type isn't common and there's no filter, or it can't connect to the
	/// query's plug.
	/// </summary>
	public static int? DefaultGetScore( INodeType nodeType, NodeQuery query )
	{
		if ( !nodeType.IsCommon && query.Filter.Count == 0 ) return null;
		if ( query.GetScore( nodeType.Path ) is not { } score ) return null;

		var plugType = GetPlugType( query.Plug );
		var matchesPlug = query.Plug switch
		{
			IPlugIn => nodeType.TryGetOutput( plugType, out _ ),
			IPlugOut => nodeType.TryGetInput( plugType, out _ ),
			_ => true
		};

		return matchesPlug ? score : null;
	}

	/// <summary>
	/// The plug's type; for an untyped reroute output, the type of whatever feeds the reroute.
	/// </summary>
	private static Type GetPlugType( IPlug plug )
	{
		var plugType = plug?.Type ?? typeof( object );

		if ( plugType == typeof( object ) )
		{
			if ( plug is IPlugOut plugOut && plugOut.Node is IRerouteNode reroute )
			{
				var connected = reroute.Inputs.FirstOrDefault()?.ConnectedOutput;
				if ( connected is not null )
				{
					return GetPlugType( connected );
				}
			}
		}

		return plugType;
	}

	/// <summary>
	/// If true, include this type in the node menu even without a search filter.
	/// </summary>
	bool IsCommon => true;

	/// <summary>
	/// Where the type sits in the menu, outermost first.
	/// </summary>
	NodePathElement[] Path { get; }

	/// <summary>
	/// The identifier of an input that takes <paramref name="valueType"/>.
	/// </summary>
	bool TryGetInput( Type valueType, out string name );

	/// <summary>
	/// The identifier of an output that gives <paramref name="valueType"/>.
	/// </summary>
	bool TryGetOutput( Type valueType, out string name );

	/// <summary>
	/// A new node of this type for <paramref name="graph"/>, not yet added to it.
	/// </summary>
	INode CreateNode( IGraph graph );

	public bool Matches( NodeQuery query ) => GetScore( query ) is not null;
	public int? GetScore( NodeQuery query ) => DefaultGetScore( this, query );
}
