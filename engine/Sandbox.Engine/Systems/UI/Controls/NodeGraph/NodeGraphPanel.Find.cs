namespace Sandbox.UI.NodeGraph;

//
// Ctrl+F: find a placed node by name and jump to it.
//
public partial class NodeGraphPanel
{
	const int MaxFindResults = 20;

	/// <summary>
	/// Open a filter box over the graph's nodes under the mouse. Picking one selects it and centres
	/// the view on it, for finding a node in a graph too big to scan by eye.
	/// </summary>
	public void OpenFindMenu()
	{
		var menu = new Menu();
		var filter = menu.AddWidget( new TextEntry { Placeholder = "Find Nodes.." } );
		filter.AddClass( "node-filter" );
		filter.OnTextEdited = text => PopulateFindMenu( menu, text );

		PopulateFindMenu( menu, null );
		OpenMenu( menu );
	}

	/// <summary>
	/// Refill the find menu with the nodes whose title or subtitle match <paramref name="filter"/>,
	/// best first, scored like the node menu.
	/// </summary>
	void PopulateFindMenu( Menu menu, string filter )
	{
		foreach ( var row in menu.Rows.Where( x => x is not TextEntry ).ToArray() )
		{
			menu.Remove( row );
			row.Delete( true );
		}

		var query = new NodeQuery( Graph, null, filter );
		var matches = _nodes
			.Where( x => !x.IsPreview )
			.Select( x => (Panel: x, Score: query.GetScore( FindPath( x ) )) )
			.Where( x => x.Score is not null )
			.OrderByDescending( x => x.Score )
			.ThenBy( x => x.Panel.DisplayInfo.Name, StringComparer.OrdinalIgnoreCase )
			.ToArray();

		foreach ( var (panel, _) in matches.Take( MaxFindResults ) )
		{
			var name = panel.Node.HasSubtitle ? $"{panel.DisplayInfo.Name} - {panel.Node.Subtitle}" : panel.DisplayInfo.Name;
			menu.AddOption( name, panel.DisplayInfo.Icon, () => GoToNode( panel ) );
		}

		if ( matches.Length > MaxFindResults )
		{
			menu.AddOption( $"... and {matches.Length - MaxFindResults} more" ).Enabled = false;
		}
	}

	static NodePathElement[] FindPath( NodePanel panel ) => panel.Node.HasSubtitle
		? [new( panel.DisplayInfo.Name ?? "" ), new( panel.Node.Subtitle )]
		: [new( panel.DisplayInfo.Name ?? "" )];

	/// <summary>
	/// Select <paramref name="panel"/> alone and centre the view on it, keeping the zoom.
	/// </summary>
	void GoToNode( NodePanel panel )
	{
		Focus();
		SelectNode( panel );
		CenterOnSelection();
	}
}
