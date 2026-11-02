using System.Diagnostics.CodeAnalysis;

#nullable enable

namespace Editor.NodeEditor;

public interface IGraph
{
	public IEnumerable<INode> Nodes { get; }

	void AddNode( INode node );
	void RemoveNode( INode node );

	string SerializeNodes( IEnumerable<INode> nodes );
	IEnumerable<INode> DeserializeNodes( string serialized );
}

public interface INodeType
{
	public static bool DefaultMatches( INodeType nodeType, NodeQuery query )
	{
		return DefaultGetScore( nodeType, query ) != null;
	}

	public static int? DefaultGetScore( INodeType nodeType, NodeQuery query )
	{
		if ( !nodeType.IsCommon && query.Filter.Count == 0 ) return null;
		if ( query.GetScore( nodeType.Path ) is not { } score ) return null;

		var plugType = GetPlugType( query.Plug );
		var matchesPlug = query.Plug switch
		{
			IPlugIn plugIn => nodeType.TryGetOutput( plugType, out _ ),
			IPlugOut plugOut => nodeType.TryGetInput( plugType, out _ ),
			_ => true
		};

		return matchesPlug ? score : null;
	}

	private static Type GetPlugType( IPlug? plug )
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
	Menu.PathElement[] Path { get; }
	bool TryGetInput( Type valueType, [NotNullWhen( true )] out string? name );
	bool TryGetOutput( Type valueType, [NotNullWhen( true )] out string? name );
	INode CreateNode( IGraph graph );

	public bool Matches( NodeQuery query ) => GetScore( query ) is not null;
	public int? GetScore( NodeQuery query ) => DefaultGetScore( this, query );
}

public interface INode
{
	event Action Changed;

	string Identifier { get; }
	DisplayInfo DisplayInfo { get; }

	bool CanClone { get; }
	bool CanRemove { get; }

	Vector2 Position { get; set; }
	Vector2 ExpandSize { get; }

	bool AutoSize { get; }

	public IEnumerable<IPlugIn> Inputs { get; }
	public IEnumerable<IPlugOut> Outputs { get; }

	public string? ErrorMessage { get; }
	public bool IsReachable { get; }

	Pixmap? Thumbnail { get; }
	void OnPaint( Rect rect );
	void OnDoubleClick( MouseEvent e );
	bool HasTitleBar { get; }

	NodeUI CreateUI( GraphView view );
	Color GetPrimaryColor( GraphView view );
	Menu? CreateContextMenu( NodeUI node );

	Action? GoToDefinition => null;
}

public interface INodeHighlight
{
	Color HighlightOutlineColor { get; }
}

public interface IRerouteNode : INode
{
	string? Comment { get; set; }
}

public interface IPlug
{
	INode Node { get; }
	string Identifier { get; }
	Type Type { get; }
	DisplayInfo DisplayInfo { get; }
	ValueEditor CreateEditor( NodeUI node, Plug plug );
	Menu? CreateContextMenu( NodeUI node, Plug plug );

	void OnDoubleClick( NodeUI node, Plug plug, MouseEvent e );

	bool ShowLabel { get; }
	bool AllowStretch { get; }
	bool ShowConnection { get; }
	bool InTitleBar { get; }
	bool IsReachable { get; }

	string ErrorMessage { get; }
}


public interface IPlugIn : IPlug
{
	IPlugOut? ConnectedOutput { get; set; }
	float? GetHandleOffset( string name );
	void SetHandleOffset( string name, float? value );
}

public interface IPlugOut : IPlug
{

}
