namespace Sandbox.UI.NodeGraph;

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
