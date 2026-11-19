namespace Editor.AnimGraph;

public partial class Window
{
	private record Page( string Title, string Icon, string NodeId = null, int SubGraph = -1, string ParameterId = null );

	private readonly List<Page> _nav = new();
	private readonly List<List<Page>> _navBack = new();
	private readonly List<List<Page>> _navForward = new();
	private readonly List<Widget> _navButtons = new();
	private readonly Dictionary<Page, (Vector2 Center, float Zoom)> _viewMemory = new();
	private readonly Dictionary<Page, object> _pageStateMemory = new();
	private Page _shownPage;
	private Widget _breadcrumb;
	private BreadcrumbBar _crumbs;
	private Widget _pageHost;
	private Widget _pageCanvas;
	private Workspace _workspace;
	private string _breadcrumbShown;

	private void NavigateToRoot()
	{
		_navBack.Clear();
		_navForward.Clear();
		_nav.Clear();
		_nav.Add( new Page( "Graph", "account_tree" ) );
		ShowPage();
	}

	/// <summary>
	/// Runs a navigation, recording the previous location so Back can return to it.
	/// </summary>
	private void Navigate( Action mutate )
	{
		var before = _nav.ToList();
		mutate();

		if ( _nav.SequenceEqual( before ) )
			return;

		_navBack.Add( before );
		_navForward.Clear();

		if ( _navBack.Count > 64 )
			_navBack.RemoveAt( 0 );
	}

	private void GoBack()
	{
		if ( _navBack.Count == 0 )
			return;

		_navForward.Add( _nav.ToList() );
		RestoreNav( _navBack[^1] );
		_navBack.RemoveAt( _navBack.Count - 1 );
	}

	private void GoForward()
	{
		if ( _navForward.Count == 0 )
			return;

		_navBack.Add( _nav.ToList() );
		RestoreNav( _navForward[^1] );
		_navForward.RemoveAt( _navForward.Count - 1 );
	}

	private void GoUp() => NavigateBackTo( _nav.Count - 2 );
	private void GoHome() => NavigateBackTo( 0 );

	/// <summary>
	/// Restores a remembered location, dropping pages that no longer resolve.
	/// </summary>
	private void RestoreNav( List<Page> path )
	{
		_nav.Clear();
		_nav.AddRange( path );

		var stale = _nav.FindIndex( page => ResolvePage( page ) is null );
		if ( stale > 0 )
			_nav.RemoveRange( stale, _nav.Count - stale );

		ShowPage();
	}

	/// <summary>
	/// Steps into a node's full-page editor, if it has one - any widget tagged
	/// <see cref="NodePageAttribute"/> for the node's type.
	/// </summary>
	internal bool NavigateInto( Node node )
	{
		if ( node is ParameterNode parameterNode && FindVirtualParameter( parameterNode ) is { } virtualParameter )
		{
			NavigateInto( virtualParameter );
			return true;
		}

		if ( IsCurrentNodePage( node ) )
			return true;

		if ( FindPageType( node ) is null )
		{
			var subGraphs = node.SubGraphs.ToList();
			if ( subGraphs.Count != 1 )
				return false;

			var subGraph = subGraphs[0];
			var (title, icon) = SubGraphInfo( node, subGraph );
			NavigateInto( subGraph, title, icon );
			return true;
		}

		Navigate( () =>
		{
			_nav.Add( new Page( node.DisplayInfo.Name, node.DisplayInfo.Icon, node.Identifier ) );
			ShowPage();
		} );

		return true;
	}

	/// <summary>
	/// The navigation action for a selection - the "open" button shown in the
	/// properties header. Add a case here for any new selection kind you can open.
	/// </summary>
	internal (string Text, Action Action)? GetNavigationAction( object selected ) => selected switch
	{
		Node node when GetNavigationActionText( node ) is { } text => (text, () => NavigateInto( node )),
		VirtualParameter parameter => ("Open Full Graph", () => NavigateInto( parameter )),
		StateMachineNode.State { Type: not StateMachineNode.StateType.Off } state => ("Open Full Graph", () => NavigateInto( state )),
		StateMachineNode.Conduit conduit => ("Open Full Graph", () => NavigateInto( conduit.Graph )),
		StateMachineCanvas.SharedSelection shared => ("Open Full Graph", () => NavigateInto( shared )),
		Nodes.Blend2DNode.BlendPoint point => ("Open Full Graph", () => NavigateInto( point.Graph )),
		Nodes.Blend1DNode.BlendPoint point => ("Open Full Graph", () => NavigateInto( point.Graph )),
		_ => null,
	};

	/// <summary>
	/// Action text for stepping into a node - "Open Full Editor" for nodes with a
	/// full-page editor, "Open Full Graph" for nodes owning a single sub graph, or
	/// null when there is nowhere to go.
	/// </summary>
	internal string GetNavigationActionText( Node node )
	{
		if ( node is ParameterNode parameterNode && FindVirtualParameter( parameterNode ) is { } virtualParameter )
			return _shownPage?.ParameterId == virtualParameter.Identifier ? null : "Open Full Graph";

		if ( IsCurrentNodePage( node ) )
			return null;

		if ( FindPageType( node ) is not null )
			return "Open Full Editor";

		return node.SubGraphs.Count() == 1 ? "Open Full Graph" : null;
	}

	internal VirtualParameter FindVirtualParameter( ParameterNode node ) =>
		_graph.VirtualParameters.FirstOrDefault( p => p.Identifier == node.ParameterIdentifier )
			?? _graph.VirtualParameters.FirstOrDefault( p => p.Type == node.ParameterType
				&& p.Name.Equals( node.ParameterName, StringComparison.OrdinalIgnoreCase ) );

	private bool IsCurrentNodePage( Node node ) =>
		_shownPage is { SubGraph: -1 }
		&& ResolvePage( _shownPage ) is Node current
		&& current.Identifier == node.Identifier;

	/// <summary>
	/// Steps into a nested graph, resolving its title and icon from the owning node.
	/// </summary>
	internal void NavigateInto( Data graph )
	{
		var owner = _graph.AllNodes.FirstOrDefault( n => n.SubGraphs.Contains( graph ) );
		if ( owner is null )
			return;

		var (title, icon) = SubGraphInfo( owner, graph );
		NavigateInto( graph, title, icon );
	}

	/// <summary>
	/// Steps into a nested graph - a state's pose graph or a transition's condition graph.
	/// </summary>
	internal void NavigateInto( Data graph, string title, string icon )
	{
		var owner = _graph.AllNodes.FirstOrDefault( n => n.SubGraphs.Contains( graph ) );
		if ( owner is null )
			return;

		Navigate( () =>
		{
			_nav.Add( new Page( title, icon, owner.Identifier, owner.SubGraphs.ToList().IndexOf( graph ) ) );
			ShowPage();
		} );
	}

	internal void NavigateInto( StateMachineNode.State state )
	{
		if ( state.Type != StateMachineNode.StateType.StateMachine || state.NestedStateMachine is not { } machine )
		{
			NavigateInto( state.Graph, state.Name, state.Type == StateMachineNode.StateType.StateMachine ? "hub" : "animation" );
			return;
		}

		Navigate( () =>
		{
			_nav.Add( new Page( state.Name, "hub", machine.Identifier ) );
			ShowPage();
		} );
	}

	internal void NavigateInto( StateMachineCanvas.SharedSelection selection )
	{
		if ( selection.GetGraph( _selectedStateMachine ) is { } graph )
			NavigateInto( graph, selection.Title, selection.Icon );
	}

	internal void NavigateInto( VirtualParameter parameter )
	{
		var existingPage = _nav.FindIndex( page => page.ParameterId == parameter.Identifier );
		if ( existingPage == _nav.Count - 1 )
			return;

		if ( existingPage >= 0 )
		{
			NavigateBackTo( existingPage );
			return;
		}

		Navigate( () =>
		{
			_nav.Add( new Page( parameter.Name, "vibration", ParameterId: parameter.Identifier ) );
			ShowPage();
		} );
	}

	internal void OnVirtualParameterRemoved( string identifier )
	{
		if ( _nav.Any( page => page.ParameterId == identifier ) )
			NavigateToRoot();
	}

	internal void OnVirtualParameterRenamed( VirtualParameter parameter )
	{
		var index = _nav.FindIndex( page => page.ParameterId == parameter.Identifier );
		if ( index < 0 )
			return;

		_nav[index] = _nav[index] with { Title = parameter.Name };
		_breadcrumbShown = null;
		RebuildBreadcrumb();
	}

	/// <summary>
	/// Jumps to a node anywhere in the graph - rebuilds the breadcrumb path to its
	/// containing page, then selects and centres on it.
	/// </summary>
	internal void NavigateToNode( string identifier )
	{
		var target = _graph.AllNodes.FirstOrDefault( n => n.Identifier == identifier );
		if ( target is null )
			return;

		var virtualParameter = _graph.VirtualParameters.FirstOrDefault( p => p.Graph.AllNodes.Contains( target ) );
		var path = FindGraphPath( virtualParameter?.Graph ?? _graph, target );
		if ( path is null )
			return;

		Navigate( () =>
		{
			_nav.Clear();
			_nav.Add( new Page( "Graph", "account_tree" ) );

			if ( virtualParameter is not null )
				_nav.Add( new Page( virtualParameter.Name, "vibration", ParameterId: virtualParameter.Identifier ) );

			foreach ( var (owner, sub) in path )
			{
				if ( FindPageType( owner ) is not null )
					_nav.Add( new Page( owner.DisplayInfo.Name, owner.DisplayInfo.Icon, owner.Identifier ) );

				var (title, icon) = SubGraphInfo( owner, sub );
				_nav.Add( new Page( title, icon, owner.Identifier, owner.SubGraphs.ToList().IndexOf( sub ) ) );
			}

			ShowPage();
		} );

		if ( _graphView.Visible )
		{
			_graphView.SelectNode( target );
			_graphView.CenterOnSelection();
		}
	}

	internal string GetNodePath( Node target )
	{
		var parts = new List<string>();
		var graph = target.Graph as Data;
		while ( graph?.Owner is { } owner )
		{
			parts.Add( SubGraphInfo( owner, graph ).Title );

			if ( FindPageType( owner ) is not null )
				parts.Add( owner.DisplayInfo.Name );

			graph = owner.Graph as Data;
		}

		var virtualParameter = _graph.VirtualParameters.FirstOrDefault( p => p.Graph == graph );
		if ( virtualParameter is not null )
			parts.Add( virtualParameter.Name );
		else if ( graph != _graph )
			return "Graph";

		parts.Add( "Graph" );
		parts.Reverse();
		return string.Join( " › ", parts );
	}

	internal void NavigateToStateMachineItem( StateMachineNode machine, object selection )
	{
		NavigateToNode( machine.Identifier );
		NavigateInto( machine );

		if ( _pageCanvas is StateMachinePage page )
			page.FocusSelection( selection );
	}

	internal void NavigateToParameter( IParameter parameter )
	{
		if ( parameter is VirtualParameter virtualParameter )
		{
			NavigateInto( virtualParameter );
			return;
		}

		DockManager.SetDockState( "Parameters", true );
		DockManager.RaiseDock( "Parameters" );
		OnParameterSelected( parameter );
		_parameterList.UpdateSelection();
	}

	/// <summary>
	/// The chain of nested graphs leading from <paramref name="graph"/> down to the
	/// one containing <paramref name="target"/> - empty when it's in the graph itself.
	/// </summary>
	private static List<(Node Owner, Data Sub)> FindGraphPath( Data graph, Node target )
	{
		if ( graph.Nodes.Contains( target ) )
			return new();

		foreach ( var node in graph.Nodes.OfType<Node>() )
		{
			foreach ( var sub in node.SubGraphs )
			{
				if ( FindGraphPath( sub, target ) is { } tail )
				{
					tail.Insert( 0, (node, sub) );
					return tail;
				}
			}
		}

		return null;
	}

	private static (string Title, string Icon) SubGraphInfo( Node owner, Data sub )
	{
		if ( owner is Blend1DNode blend1D )
		{
			var pointIndex = blend1D.Points.FindIndex( point => ReferenceEquals( point.Graph, sub ) );
			if ( pointIndex >= 0 )
				return (blend1D.PointTitle( pointIndex ), "linear_scale");
		}

		if ( owner is Blend2DNode blend )
		{
			var pointIndex = blend.Points.FindIndex( point => ReferenceEquals( point.Graph, sub ) );
			if ( pointIndex >= 0 )
				return (blend.PointTitle( pointIndex ), "grain");
		}

		if ( owner is StateMachineNode machine )
		{
			if ( machine.States.FirstOrDefault( s => s.Graph == sub ) is { } state )
				return (state.Name, state.Type == StateMachineNode.StateType.StateMachine ? "hub" : "animation");

			if ( machine.States.FirstOrDefault( s => s.LayerGraph == sub ) is { } layer )
				return ($"{layer.Name} Layer Data", "layers");

			if ( machine.Conduits.FirstOrDefault( c => c.Graph == sub ) is { } conduit )
				return ($"{conduit.From} → {conduit.To}", "route");

			if ( sub == machine.GlobalGraph )
				return ("Global Transitions", "public");

			if ( sub == machine.EntryGraph )
				return ("Entry Overrides", "login");
		}

		if ( owner is SelectorNode )
			return ("Conditions", "rule");

		return (owner.DisplayInfo.Name, owner.DisplayInfo.Icon);
	}

	/// <summary>
	/// Finds what a page shows in the current graph - by node identity, so pages
	/// stay valid across undo and redo rebuilding the graph.
	/// </summary>
	private object ResolvePage( Page page )
	{
		if ( page.ParameterId is not null )
			return _graph.VirtualParameters.FirstOrDefault( p => p.Identifier == page.ParameterId )?.Graph;

		if ( page.NodeId is null )
			return _graph;

		var node = _graph.AllNodes.FirstOrDefault( n => n.Identifier == page.NodeId );
		if ( node is null )
			return null;

		return page.SubGraph < 0 ? node : node.SubGraphs.ElementAtOrDefault( page.SubGraph );
	}

	private void NavigateBackTo( int index )
	{
		if ( index < 0 || index >= _nav.Count - 1 )
			return;

		Navigate( () =>
		{
			_nav.RemoveRange( index + 1, _nav.Count - index - 1 );
			ShowPage();
		} );
	}

	internal static TypeDescription FindPageType( Node node ) => node is null ? null
		: EditorTypeLibrary.GetTypesWithAttribute<NodePageAttribute>()
			.FirstOrDefault( t => t.Attribute.NodeType == node.GetType() ).Type;

	/// <summary>
	/// Remembers the shown page's view so navigating back returns you to where you left off.
	/// </summary>
	private void SaveViewMemory()
	{
		if ( _shownPage is null )
			return;

		if ( (_pageCanvas as INodePage)?.ViewCanvas is { } canvas && canvas.Zoom > 0 )
			_viewMemory[_shownPage] = (canvas.ViewCenter, canvas.Zoom);
		else if ( _graphView?.Visible ?? false )
			_viewMemory[_shownPage] = (_graphView.Center, _graphView.Scale.x);

		if ( _pageCanvas is StateMachinePage stateMachinePage )
		{
			if ( stateMachinePage.CaptureSelection() is { } state )
				_pageStateMemory[_shownPage] = state;
			else
				_pageStateMemory.Remove( _shownPage );
		}
	}

	private void ShowPage()
	{
		SaveViewMemory();

		var page = _nav[^1];
		var content = ResolvePage( page );
		_shownPage = page;
		_properties?.NavigationContextChanged();

		// Rebind the open page in place when it still shows the same node -
		// keeps view position and selection, no widget churn, no flicker
		if ( content is Node node && _pageCanvas is INodePage open && open.Rebind( node ) )
		{
			RebuildBreadcrumb();
			return;
		}

		_pageCanvas?.Destroy();
		_pageCanvas = null;

		if ( content is Data graph )
		{
			_graphView.Graph = graph;
			_graphView.Visible = true;

			if ( _viewMemory.TryGetValue( page, out var view ) )
				_graphView.SetView( view.Center, view.Zoom );
			else
				_graphView.FitView();
		}
		else if ( content is Node n && FindPageType( n ) is { } pageType )
		{
			_graphView.Visible = false;
			_pageCanvas = pageType.Create<Widget>( [_pageHost, n, this] );
			_pageHost.Layout.Add( _pageCanvas, 1 );

			if ( (_pageCanvas as INodePage)?.ViewCanvas is { } canvas && _viewMemory.TryGetValue( page, out var view ) )
				canvas.RestoreView( view.Center, view.Zoom );

			if ( _pageCanvas is StateMachinePage stateMachinePage && _pageStateMemory.TryGetValue( page, out var state ) )
				stateMachinePage.RestoreSelection( state );
		}

		_workspace.SecondaryViewEnabled = content is Data
			|| (_pageCanvas as INodePage)?.SupportsSecondaryView == true;

		if ( content is Data )
			_graphView.NotifySelection();
		else if ( _pageCanvas is StateMachinePage stateMachinePage )
			stateMachinePage.NotifySelection();
		else
			OnCanvasSelection( content, null );

		RebuildBreadcrumb();
	}

	private void FitCurrentView()
	{
		if ( _graphView.Visible )
		{
			_graphView.FitView();
			return;
		}

		(_pageCanvas as INodePage)?.ViewCanvas?.FitView();
	}

	private void RebuildBreadcrumb()
	{
		foreach ( var button in _navButtons )
			button.Update();

		if ( _crumbs is null )
			return;

		var titles = _nav.Select( page => ResolvePage( page ) switch
		{
			Node node => node.DisplayInfo.Name,
			Data { Owner: { } owner } graph => SubGraphInfo( owner, graph ).Title,
			_ => page.Title
		} ).ToArray();
		var shown = string.Join( "|", _nav.Select( ( page, index ) => $"{page}:{titles[index]}" ) );
		if ( shown == _breadcrumbShown )
			return;

		_breadcrumbShown = shown;
		using var _ = SuspendUpdates.For( _breadcrumb );
		_crumbs.SetItems( _nav.Select( ( page, index ) => new BreadcrumbBar.Item(
			titles[index], page.Icon, PageColor( page ), index < _nav.Count - 1 ? () => NavigateBackTo( index ) : null ) ) );
	}

	/// <summary>
	/// A crumb's icon tint - the owning node's colour.
	/// </summary>
	private Color? PageColor( Page page )
	{
		if ( page.NodeId is null )
			return null;

		return _graph.AllNodes.FirstOrDefault( n => n.Identifier == page.NodeId )?.PrimaryColor;
	}

}
