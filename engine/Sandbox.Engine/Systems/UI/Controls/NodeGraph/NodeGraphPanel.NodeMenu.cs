namespace Sandbox.UI.NodeGraph;

//
// The node menu: a filter box over the types GetRelevantNodes offers.
//
public partial class NodeGraphPanel
{
	/// <summary>
	/// The node types the node menu offers for <paramref name="query"/>, such as
	/// <c>AllTypes.Filter( query )</c>. None by default.
	/// </summary>
	protected virtual IEnumerable<INodeType> GetRelevantNodes( NodeQuery query ) => [];

	/// <summary>
	/// Entries above the node types while the filter is empty: paste, and add a comment - or a
	/// reroute, for a wire dropped from <paramref name="targetPlug"/>.
	/// </summary>
	protected virtual void OnPopulateNodeMenuSpecialOptions( Menu menu, Vector2 position, PlugPanel targetPlug, string filter )
	{
		if ( ReadClipboard() is { } text && text.StartsWith( $"{ClipboardIdent}:", StringComparison.Ordinal ) )
		{
			menu.AddOption( "Paste Node(s)", "content_paste", () => Paste( text, position ) );
		}

		if ( !targetPlug.IsValid() )
		{
			if ( CommentNodeType is not null )
			{
				menu.AddOption( "Add Comment", "notes", () => CreateNewComment( "Untitled", DefaultCommentColor, position, DefaultCommentSize ) );
			}
		}
		else if ( RerouteNodeType is not null )
		{
			menu.AddOption( "Add Reroute", "route", () => CreateNewNode( RerouteNodeType, position, targetPlug ) );
		}
	}

	/// <summary>
	/// The node types matching <paramref name="filter"/> and <paramref name="targetPlug"/>: as
	/// submenus by path, or a flat list of the best 20 while filtering. Picking one adds it at
	/// <paramref name="position"/>, wired to the plug.
	/// </summary>
	protected virtual void OnPopulateNodeMenu( Menu menu, Vector2 position, PlugPanel targetPlug, string filter )
	{
		if ( Graph is null ) return;

		var query = new NodeQuery( Graph, targetPlug.IsValid() ? targetPlug.Inner : null, filter );
		var nodes = GetRelevantNodes( query ).ToArray();

		var useFilter = query.Filter.Count > 0;
		var truncated = 0;

		const int maxFilteredResults = 20;

		if ( useFilter && nodes.Length > maxFilteredResults )
		{
			truncated = nodes.Length - maxFilteredResults;
			nodes = nodes.OrderByDescending( x => query.GetScore( x.Path ) ?? int.MinValue ).Take( maxFilteredResults ).ToArray();
		}

		NodeMenu.AddOptions( menu, nodes, useFilter ? query : null, type => CreateNewNode( type, position, targetPlug ) );

		if ( truncated > 0 )
		{
			menu.AddOption( $"... and {truncated} more" ).Enabled = false;
		}
	}

	/// <summary>
	/// Refill the node menu for <paramref name="filter"/>, keeping its filter box.
	/// </summary>
	protected virtual void PopulateNodeMenu( Menu menu, Vector2 position, PlugPanel targetPlug = null, string filter = null )
	{
		foreach ( var row in menu.Rows.Where( x => x is not TextEntry ).ToArray() )
		{
			menu.Remove( row );
			row.Delete( true );
		}

		if ( string.IsNullOrWhiteSpace( filter ) )
		{
			OnPopulateNodeMenuSpecialOptions( menu, position, targetPlug, filter );
		}

		OnPopulateNodeMenu( menu, position, targetPlug, filter );
	}

	/// <summary>
	/// Make <paramref name="menu"/> the node menu: a filter box over what
	/// <see cref="PopulateNodeMenu"/> fills in.
	/// </summary>
	protected virtual void CreateNodeMenu( Menu menu, Vector2 position, PlugPanel targetPlug = null )
	{
		var filter = menu.AddWidget( new TextEntry { Placeholder = "Filter Nodes.." } );
		filter.AddClass( "node-filter" );
		filter.OnTextEdited = text => PopulateNodeMenu( menu, position, targetPlug, text );

		PopulateNodeMenu( menu, position, targetPlug );
	}
}
