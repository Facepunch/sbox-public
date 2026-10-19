namespace Sandbox.UI.NodeGraph;

/// <summary>
/// Shows an <see cref="IGraph"/> as nodes and wires, like the editor's node graph. Middle-drag
/// pans, the wheel zooms, left-drag on empty space box-selects, Delete removes the selection.
/// </summary>
[StyleSheet.Inline( "nodegraphpanel", ".nodegraphpanel { min-width: 0; min-height: 0; } .nodegraphpanel .canvas-selection-box { border: 1px solid #4C8DFF; background-color: rgba( 76, 141, 255, 0.1 ); border-radius: 4px; }" )]
public partial class NodeGraphPanel : CanvasPanel
{
	/// <summary>
	/// Nodes snap to this, in canvas units.
	/// </summary>
	public float GridSize { get; set; } = 12f;

	/// <summary>
	/// Fade the grid out as the view zooms out.
	/// </summary>
	public virtual bool FadeOutBackground => true;

	IGraph _graph;

	/// <summary>
	/// The graph shown. Setting a different one rebuilds the view; call <see cref="RebuildFromGraph"/>
	/// to refresh the same one.
	/// </summary>
	public IGraph Graph
	{
		get => _graph;
		set
		{
			if ( ReferenceEquals( _graph, value ) ) return;

			_graph = value;
			RebuildFromGraph();
		}
	}

	readonly List<NodePanel> _nodes = new();

	public IReadOnlyList<NodePanel> Nodes => _nodes;

	public IEnumerable<NodePanel> SelectedNodes => _nodes.Where( x => x.Selected );

	/// <summary>
	/// The node selection changed.
	/// </summary>
	public event Action SelectionChanged;

	bool _viewInitialized;

	public NodeGraphPanel()
	{
		AddClass( "nodegraphpanel" );
		BoxSelectionEnabled = true;
		MinimumViewSpan = 10;
		MaximumViewSpan = 100000;

		// Named methods, not lambdas: hotload can't remap lambdas held by a live panel
		BoxSelectionStarted += OnBoxSelectionStarted;
		BoxSelectionChanged += OnBoxSelectionChanged;
		BoxSelectionFinished += OnBoxSelectionFinished;
		OnTooltip = BuildConnectionTooltip;
	}

	HashSet<NodePanel> _boxBase;

	/// <summary>
	/// A box selection began. Shift-dragging (<paramref name="additive"/>) keeps the current selection.
	/// Overrides should call the base, which the rest of the gesture relies on.
	/// </summary>
	protected virtual void OnBoxSelectionStarted( bool additive )
	{
		_boxBase = additive ? SelectedNodes.ToHashSet() : [];
		if ( !additive ) DeselectConnections();
	}

	/// <summary>
	/// A box selection ended or was <paramref name="cancelled"/>. Overrides should call the base.
	/// </summary>
	protected virtual void OnBoxSelectionFinished( bool cancelled ) => _boxBase = null;

	/// <summary>
	/// Called before a user edit changes the graph; <see cref="PushRedo"/> follows once it's done.
	/// Hosts with undo override both.
	/// </summary>
	public virtual void PushUndo( string name ) { }

	/// <summary>
	/// Called after the edit announced by <see cref="PushUndo"/>.
	/// </summary>
	public virtual void PushRedo() { }

	int _undoDepth;

	/// <summary>
	/// <see cref="PushUndo"/> now and <see cref="PushRedo"/> on dispose. Inside another scope it
	/// does neither, so one user edit is one undo step.
	/// </summary>
	internal IDisposable UndoScope( string name )
	{
		if ( _undoDepth++ == 0 ) PushUndo( name );
		return new Sandbox.Utility.DisposeAction( EndUndoScope );
	}

	void EndUndoScope()
	{
		if ( --_undoDepth == 0 ) PushRedo();
	}

	/// <summary>
	/// Layout pixels per canvas unit; 1 at the default zoom.
	/// </summary>
	public float Zoom => (CanvasToScreen( Vector2.One ) - CanvasToScreen( Vector2.Zero )).x / ScaleToScreen;

	/// <summary>
	/// The furthest the wheel zooms out, as a <see cref="Zoom"/>.
	/// </summary>
	public float MinZoom { get; set; } = 0.1f;

	/// <summary>
	/// The furthest the wheel zooms in, as a <see cref="Zoom"/>.
	/// </summary>
	public float MaxZoom { get; set; } = 5f;

	/// <summary>
	/// Outline and glow of selected nodes and comments, and the colour of selected wires.
	/// </summary>
	public Color SelectionColor { get; set; } = NodeTheme.Accent;

	/// <summary>
	/// Outline of a node under the cursor.
	/// </summary>
	public Color HoverBorderColor { get; set; } = NodeTheme.HairlineHover;

	/// <summary>
	/// Outline of a node that isn't selected, hovered or in error.
	/// </summary>
	public Color BorderColor { get; set; } = NodeTheme.Hairline;

	/// <summary>
	/// Outline and glow of a <see cref="NodePanel.Highlighted"/> node. Apart from the selection's, so
	/// both read at once.
	/// </summary>
	public Color HighlightColor { get; set; } = Color.Parse( "#F5B942" ).Value;

	/// <summary>
	/// Centre the view on <paramref name="center"/> at <paramref name="zoom"/>.
	/// </summary>
	public void SetView( Vector2 center, float zoom )
	{
		var half = Viewport.Size / ScaleToScreen / zoom * 0.5f;
		SetView( center - half, center + half );
	}

	public NodeHandleConfig DefaultHandleConfig { get; } = new( null, Color.Parse( "#999" ).Value );

	readonly Dictionary<Type, NodeHandleConfig> _handleConfigCache = new();

	protected virtual NodeHandleConfig OnGetHandleConfig( Type type ) => DefaultHandleConfig;

	/// <summary>
	/// How plugs and wires of <paramref name="type"/> look.
	/// </summary>
	public NodeHandleConfig GetHandleConfig( Type type )
	{
		if ( _handleConfigCache.TryGetValue( type, out var config ) )
		{
			return config;
		}

		config = OnGetHandleConfig( type );

		return _handleConfigCache[type] = config with { Name = config.Name ?? type.ToSimpleString( false ).HtmlEncode() };
	}

	/// <summary>
	/// The panel for <paramref name="node"/>: a <see cref="ReroutePanel"/> for reroutes, a
	/// <see cref="CommentPanel"/> for comments.
	/// </summary>
	protected virtual NodePanel CreateNodePanel( INode node, bool isPreview = false ) => node switch
	{
		IRerouteNode => new ReroutePanel( this, node, isPreview ),
		ICommentNode => new CommentPanel( this, node, isPreview ),
		_ => new NodePanel( this, node, isPreview ),
	};

	/// <summary>
	/// A see-through panel for <paramref name="node"/> that isn't in the graph, such as a drop preview.
	/// Delete it when done.
	/// </summary>
	public NodePanel CreatePreview( INode node ) => Content.AddChild( CreateNodePanel( node, true ) );

	public NodePanel FindNode( INode node ) => _nodes.FirstOrDefault( x => x.Node == node );

	public PlugPanel FindPlug( IPlug plug )
	{
		if ( plug is null || FindNode( plug.Node ) is not { } node ) return null;

		return plug is IPlugIn
			? node.Inputs.FirstOrDefault( x => x.Inner == plug )
			: node.Outputs.FirstOrDefault( x => x.Inner == plug );
	}

	/// <summary>
	/// Add <paramref name="node"/> to the graph and show it.
	/// </summary>
	public NodePanel AddNode( INode node )
	{
		ArgumentNullException.ThrowIfNull( node );

		node.Position = node.Position.SnapToGrid( GridSize );
		Graph.AddNode( node );

		return ShowNode( node );
	}

	NodePanel ShowNode( INode node )
	{
		var panel = Content.AddChild( CreateNodePanel( node ) );
		_nodes.Add( panel );
		return panel;
	}

	/// <summary>
	/// What the "Add Reroute" entries, R-dropped wires and Shift-clicked wires create. Null turns
	/// reroutes off.
	/// </summary>
	protected virtual INodeType RerouteNodeType => null;

	/// <summary>
	/// What "Add Comment" creates; must make <see cref="ICommentNode"/>s. Null turns comments off.
	/// </summary>
	protected virtual INodeType CommentNodeType => null;

	/// <summary>
	/// A node made by <see cref="CreateNewNode(INodeType, Action{INode})"/> was added to the graph.
	/// </summary>
	protected virtual void OnNodeCreated( INode node ) { }

	/// <summary>
	/// Create a node of <paramref name="type"/> at <paramref name="position"/>, snapped to the grid,
	/// add it to the graph and show it. Null when there's no type or graph.
	/// </summary>
	public virtual NodePanel CreateNewNode( INodeType type, Vector2 position )
	{
		return CreateNewNode( type, node => node.Position = position.SnapToGrid( GridSize ) );
	}

	/// <summary>
	/// Create a node of <paramref name="type"/>, set it up with <paramref name="onCreated"/>, add it
	/// to the graph and show it. Null when there's no type or graph, or the type made nothing.
	/// </summary>
	public virtual NodePanel CreateNewNode( INodeType type, Action<INode> onCreated = null )
	{
		if ( type is null || Graph is null ) return null;

		var node = type.CreateNode( Graph );
		if ( node is null ) return null;

		onCreated?.Invoke( node );

		Graph.AddNode( node );
		OnNodeCreated( node );

		return ShowNode( node );
	}

	/// <summary>
	/// Add a node of <paramref name="type"/> at <paramref name="position"/>, as one undo step, and
	/// wire it to <paramref name="targetPlug"/> when it has a matching plug.
	/// </summary>
	public virtual void CreateNewNode( INodeType type, Vector2 position, PlugPanel targetPlug, bool selected = true )
	{
		using var undoScope = UndoScope( "Add Node" );

		var panel = CreateNewNode( type, position );
		if ( panel is null ) return;

		if ( selected ) SelectNode( panel );

		if ( !targetPlug.IsValid() ) return;

		if ( targetPlug is PlugInPanel plugIn )
		{
			if ( !type.TryGetOutput( plugIn.Inner.Type, out var targetName ) ) return;

			if ( panel.Outputs.FirstOrDefault( x => x.Inner.Identifier == targetName ) is { } match )
			{
				// An input takes one wire: drop the old one now rather than on the next sync
				RemoveConnections( plugIn );
				CreateConnection( match, plugIn );
			}
		}
		else if ( targetPlug is PlugOutPanel plugOut )
		{
			if ( !type.TryGetInput( plugOut.Inner.Type, out var targetName ) ) return;

			if ( panel.Inputs.FirstOrDefault( x => x.Inner.Identifier == targetName ) is { } match )
			{
				CreateConnection( plugOut, match );
			}
		}
	}

	/// <summary>
	/// Add a reroute centred on <paramref name="position"/>.
	/// </summary>
	public void CreateNewReroute( Vector2 position )
	{
		using var undoScope = UndoScope( "Add Reroute" );

		CreateNewNode( RerouteNodeType, position - new Vector2( ReroutePanel.Diameter * 0.5f ) );
	}

	/// <summary>
	/// Remove a node, and its wires, from the graph. Nodes that can't be removed stay.
	/// </summary>
	public virtual void RemoveNode( NodePanel node )
	{
		if ( node is null || !node.Node.CanRemove || !_nodes.Contains( node ) ) return;

		using var undoScope = UndoScope( "Delete Node" );

		_nodes.Remove( node );
		OnNodeRemoving( node );
		Graph?.RemoveNode( node.Node );
		node.Delete( true );

		if ( node.Selected ) SelectionChanged?.Invoke();
	}

	partial void OnNodeRemoving( NodePanel node );

	/// <summary>
	/// Fix up the graph, such as replacing obsolete nodes. Does nothing by default.
	/// </summary>
	public virtual void CleanUp() { }

	/// <summary>
	/// Rebuild every node panel and wire from <see cref="Graph"/>.
	/// </summary>
	public virtual void RebuildFromGraph()
	{
		var hadSelection = _nodes.Any( x => x.Selected );

		OnClear();

		foreach ( var node in _nodes )
		{
			node.Delete( true );
		}

		_nodes.Clear();
		OnCleared();

		if ( Graph is not null )
		{
			BuildFromNodes( Graph.Nodes, insert: true );
		}

		OnRebuild();

		if ( _viewInitialized ) RestoreViewFromCookie();
		if ( hadSelection ) SelectionChanged?.Invoke();
	}

	/// <summary>
	/// The view is about to drop every node panel, in <see cref="RebuildFromGraph"/>.
	/// </summary>
	protected virtual void OnClear() { }

	/// <summary>
	/// <see cref="RebuildFromGraph"/> has shown the graph again.
	/// </summary>
	protected virtual void OnRebuild() { }

	partial void OnCleared();

	/// <summary>
	/// Show <paramref name="nodes"/> and their wires. When <paramref name="insert"/>, they're new:
	/// each moves by <paramref name="offset"/> and gets a panel. Otherwise they're the whole graph:
	/// panels of nodes not among them go, the rest re-read their node, and missing ones are added.
	/// <paramref name="selectNew"/> selects the new panels and the wires on them.
	/// </summary>
	public virtual void BuildFromNodes( IEnumerable<INode> nodes, bool insert, Vector2 offset = default, bool selectNew = false )
	{
		var set = new HashSet<INode>();
		var ordered = new List<INode>();

		foreach ( var node in nodes )
		{
			if ( node is not null && set.Add( node ) ) ordered.Add( node );
		}

		if ( !insert )
		{
			foreach ( var panel in _nodes.Where( x => !set.Contains( x.Node ) ).ToArray() )
			{
				_nodes.Remove( panel );
				panel.Delete( true );
			}
		}

		foreach ( var node in ordered )
		{
			if ( !insert && FindNode( node ) is { } existing )
			{
				existing.Rebuild();
				continue;
			}

			if ( offset != default ) node.Position += offset;

			var panel = ShowNode( node );
			if ( selectNew ) panel.Selected = true;
		}

		UpdateConnections( ordered, selectNew );

		if ( selectNew && ordered.Count > 0 ) SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Match the wires to the model, then lay out those on <paramref name="nodes"/>, selecting them
	/// too when <paramref name="selectNew"/>.
	/// </summary>
	public void UpdateConnections( IEnumerable<INode> nodes, bool selectNew = false )
	{
		SyncConnections();

		var set = nodes as ISet<INode> ?? nodes.ToHashSet();

		foreach ( var connection in _connections )
		{
			if ( !connection.IsValid ) continue;
			if ( !set.Contains( connection.Input.Node.Node ) && !set.Contains( connection.Output.Node.Node ) ) continue;

			connection.Layout();
			if ( selectNew ) connection.Selected = true;
		}
	}

	/// <summary>
	/// Re-read <paramref name="node"/>'s plugs and position.
	/// </summary>
	public void UpdateNode( INode node ) => FindNode( node )?.MarkNodeChanged();

	/// <summary>
	/// Select <paramref name="node"/> for a press: Ctrl toggles it, Shift adds it, otherwise it
	/// becomes the only selection unless it's already selected.
	/// </summary>
	public void PressNode( NodePanel node, bool ctrl, bool shift )
	{
		if ( ctrl )
		{
			node.Selected = !node.Selected;
		}
		else if ( shift )
		{
			node.Selected = true;
		}
		else if ( !node.Selected )
		{
			DeselectConnections();
			foreach ( var other in _nodes ) other.Selected = other == node;
		}
		else
		{
			return;
		}

		SelectionChanged?.Invoke();
	}

	public void SelectNode( NodePanel node )
	{
		DeselectConnections();
		foreach ( var other in _nodes ) other.Selected = other == node;
		SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Make the panel showing <paramref name="node"/> the only selection. Does nothing if it isn't shown.
	/// </summary>
	public void SelectNode( INode node )
	{
		if ( node is not null && FindNode( node ) is { } panel )
			SelectNode( panel );
	}

	/// <summary>
	/// Select every node and wire.
	/// </summary>
	public void SelectAll()
	{
		foreach ( var node in _nodes ) node.Selected = true;
		foreach ( var connection in _connections ) connection.Selected = true;
		SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Centre the view on the selected nodes and wires, keeping the zoom.
	/// </summary>
	public void CenterOnSelection()
	{
		var min = new Vector2( float.MaxValue );
		var max = new Vector2( float.MinValue );
		var any = false;

		foreach ( var node in SelectedNodes )
		{
			min = Vector2.Min( min, node.Position );
			max = Vector2.Max( max, node.Position + node.Size );
			any = true;
		}

		foreach ( var connection in SelectedConnections )
		{
			min = Vector2.Min( min, connection.Bounds.TopLeft );
			max = Vector2.Max( max, connection.Bounds.BottomRight );
			any = true;
		}

		if ( !any ) return;

		SetView( (min + max) * 0.5f, Zoom );
	}

	/// <summary>
	/// Deselect every node and wire.
	/// </summary>
	public void ClearSelection()
	{
		if ( DeselectNodes() | DeselectConnections() ) SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Deselect every node without raising <see cref="SelectionChanged"/>; true if any was selected.
	/// </summary>
	bool DeselectNodes()
	{
		var any = false;

		foreach ( var node in _nodes )
		{
			any |= node.Selected;
			node.Selected = false;
		}

		return any;
	}

	/// <summary>
	/// Remove the selected wires, and every selected node that can be removed, as one undo step.
	/// </summary>
	public virtual void DeleteSelection()
	{
		if ( !_nodes.Any( x => x.Selected ) && !_connections.Any( x => x.Selected ) ) return;

		using var undoScope = UndoScope( "Delete Selection" );

		OnDeleteSelection();

		foreach ( var node in SelectedNodes.Where( x => x.Node.CanRemove ).ToArray() )
		{
			RemoveNode( node );
		}
	}

	partial void OnDeleteSelection();

	/// <summary>
	/// The box moved, in canvas units. Any overlap selects a node. A comment needs the whole of it
	/// inside the box, so selecting nodes in a comment doesn't drag the comment and everything in it along.
	/// </summary>
	protected virtual void OnBoxSelectionChanged( Rect box )
	{
		foreach ( var node in _nodes )
		{
			var rect = new Rect( node.Position, node.Size );
			var inBox = node is CommentPanel ? box.IsInside( rect, true ) : rect.Overlaps( box );
			node.Selected = _boxBase.Contains( node ) || inBox;
		}

		SelectionChanged?.Invoke();
	}

	/// <summary>
	/// A text field in the graph, such as an inline editor, has the keyboard.
	/// </summary>
	bool TextEntryFocused => Descendants.OfType<TextEntry>().Any( x => x.HasFocus );

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Button == "delete" && !IsBoxSelecting )
		{
			DeleteSelection();
			e.StopPropagation = true;
			return;
		}

		// Ctrl+C, X and V come as copy, cut and paste events; D is ours, unless a text field wants it
		if ( e.HasCtrl && e.Button == "d" && !IsBoxSelecting && !TextEntryFocused )
		{
			DuplicateSelection();
			e.StopPropagation = true;
			return;
		}

		// Space opens the node menu at the mouse, like right-clicking empty space
		if ( e.Button == "space" && Graph is not null && !IsBoxSelecting && !TextEntryFocused )
		{
			OpenContextMenu( MouseTarget, null );
			e.StopPropagation = true;
			return;
		}

		base.OnButtonTyped( e );
	}

	/// <summary>
	/// Holding R makes a wire dropped on empty space end in a reroute instead of opening the menu.
	/// </summary>
	bool _createReroute;

	public override void OnButtonEvent( ButtonEvent e )
	{
		if ( e.Button == "r" && !TextEntryFocused ) _createReroute = e.Pressed;

		base.OnButtonEvent( e );
	}

	/// <summary>
	/// Blurs arrive late and bubble up from whatever lost focus - a press on a plug focuses its
	/// node and then the graph - so look at where the focus is now. R's release won't reach the
	/// graph once a text field or something outside has the keyboard.
	/// </summary>
	protected override void OnBlur( PanelEvent e )
	{
		base.OnBlur( e );

		var focus = UISystem?.CurrentFocus;
		if ( focus is null || focus is TextEntry || !focus.AncestorsAndSelf.Contains( this ) ) _createReroute = false;
	}

	public override void OnMouseWheel( Vector2 value )
	{
		var delta = value.y != 0 ? value.y : value.x;
		if ( delta == 0 || !CanNavigate || IsPanning || IsBoxSelecting || !Viewport.IsInside( MousePosition ) ) return;

		// ZoomAt scales the visible span, so the zoom changes by its inverse. Stop on the limit
		// rather than short of it.
		var zoom = Zoom;
		var target = (zoom * (delta < 0 ? 1.1f : 0.9f)).Clamp( MinZoom, MaxZoom );
		if ( target.AlmostEqual( zoom ) ) return;

		ZoomAt( MousePosition, zoom / target );
	}

	public override void Tick()
	{
		if ( !_viewInitialized && Viewport.Width > 0 && Viewport.Height > 0 )
		{
			_viewInitialized = true;
			SetView( Vector2.Zero, 1f );
			RestoreViewFromCookie();
		}

		if ( _viewInitialized )
		{
			AutoPan();
			SaveViewCookie();
		}

		if ( _nodePreview.IsValid() && _previewFollowsMouse )
		{
			_nodePreview.MoveTo( MouseCanvas.SnapToGrid( GridSize ) );
		}

		base.Tick();
	}

	/// <summary>
	/// While something is dragged past the edge of the view, scroll towards it.
	/// </summary>
	void AutoPan()
	{
		var dragging = IsBoxSelecting || _plugDragging || _wireDragging || _handleConnection is not null || _nodes.Any( x => x.IsDragging );
		if ( !dragging ) return;

		var viewport = Viewport;
		var mouse = MousePosition;
		if ( viewport.IsInside( mouse ) ) return;

		var overshoot = mouse - mouse.Clamp( viewport.Position, viewport.Position + viewport.Size );
		Pan( -overshoot / 8f );
	}

	//
	// View persistence
	//

	/// <summary>
	/// Cookie name the view's centre and zoom are kept under. Null doesn't keep them.
	/// </summary>
	protected virtual string ViewCookie => null;

	Vector2 _savedCenter;
	float _savedZoom;

	/// <summary>
	/// Go back to the centre and zoom saved under <see cref="ViewCookie"/>.
	/// </summary>
	public void RestoreViewFromCookie()
	{
		if ( ViewCookie is not { } cookie || Game.Cookies is not { } cookies ) return;

		var center = cookies.Get( $"{cookie}.view.center", (ViewMin + ViewMax) * 0.5f );
		var zoom = cookies.Get( $"{cookie}.view.scale", Zoom );

		if ( !float.IsFinite( center.x ) || !float.IsFinite( center.y ) || !float.IsFinite( zoom ) ) return;

		SetView( center, zoom.Clamp( MinZoom, MaxZoom ) );

		_savedCenter = (ViewMin + ViewMax) * 0.5f;
		_savedZoom = Zoom;
	}

	void SaveViewCookie()
	{
		if ( ViewCookie is not { } cookie || Game.Cookies is not { } cookies ) return;

		var center = (ViewMin + ViewMax) * 0.5f;
		var zoom = Zoom;

		if ( center != _savedCenter )
		{
			_savedCenter = center;
			cookies.Set( $"{cookie}.view.center", center );
		}

		if ( zoom != _savedZoom )
		{
			_savedZoom = zoom;
			cookies.Set( $"{cookie}.view.scale", zoom );
		}
	}

	//
	// Dropping node types from elsewhere
	//

	/// <summary>
	/// The node type a drag carries, or null when it carries none. <paramref name="e"/> is an
	/// in-app drag's "ondragenter" or "ondrop", whose target is the dragged panel, or a
	/// <see cref="DropEvent"/> from outside the app.
	/// </summary>
	protected virtual INodeType NodeTypeFromDrop( PanelEvent e ) => null;

	NodePanel _nodePreview;
	bool _previewFollowsMouse;

	/// <summary>
	/// Where a drag is, in canvas units.
	/// </summary>
	Vector2 DropCanvas( PanelEvent e ) => e is DropEvent drop ? ScreenToCanvas( drop.Position - Box.Rect.Position ) : MouseCanvas;

	void ShowDropPreview( INodeType type, Vector2 position )
	{
		ClearDropPreview();

		if ( Graph is null || type.CreateNode( Graph ) is not { } node ) return;

		node.Position = position.SnapToGrid( GridSize );
		_nodePreview = CreatePreview( node );
	}

	void ClearDropPreview()
	{
		_nodePreview?.Delete( true );
		_nodePreview = null;
	}

	protected override void OnDragEnter( PanelEvent e )
	{
		base.OnDragEnter( e );

		if ( _nodePreview.IsValid() || NodeTypeFromDrop( e ) is not { } type ) return;

		_previewFollowsMouse = true;
		ShowDropPreview( type, MouseCanvas );
	}

	protected override void OnDragLeave( PanelEvent e )
	{
		base.OnDragLeave( e );
		ClearDropPreview();
	}

	protected override void OnDrop( PanelEvent e )
	{
		base.OnDrop( e );

		var position = DropCanvas( e );

		// A drag from outside the app asks while it hovers: answer, and show where the node would go
		if ( e is DropEvent { IsDrop: false } hover )
		{
			if ( NodeTypeFromDrop( e ) is not { } hoverType ) return;

			hover.Action = DropAction.Copy;
			_previewFollowsMouse = false;

			if ( !_nodePreview.IsValid() ) ShowDropPreview( hoverType, position );
			else _nodePreview.MoveTo( position.SnapToGrid( GridSize ) );

			e.StopPropagation();
			return;
		}

		ClearDropPreview();

		if ( NodeTypeFromDrop( e ) is not { } type ) return;

		Focus();
		CreateNewNode( type, position, null );
		e.StopPropagation();
	}

	public override void OnDraw( Painter painter )
	{
		var viewport = Viewport;

		if ( viewport.Width <= 0 || viewport.Height <= 0 )
			return;

		using var scope = painter.Scope();
		painter.Clip( viewport );

		painter.Fill = NodeTheme.Canvas;
		painter.Stroke = Stroke.None;
		painter.Rect( viewport );

		DrawGrid( painter, viewport );

		// Before the nodes draw: they paint the wires laid out here
		UpdateConnections();
	}

	static Texture _dot;

	/// <summary>
	/// One soft white dot in the middle of a tile.
	/// </summary>
	static Texture DotTexture()
	{
		if ( _dot is not null ) return _dot;

		const int size = 32;
		var data = new byte[size * size * 4];

		for ( var y = 0; y < size; y++ )
		{
			for ( var x = 0; x < size; x++ )
			{
				var distance = new Vector2( x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f ).Length;
				var i = (y * size + x) * 4;

				data[i] = data[i + 1] = data[i + 2] = 255;
				data[i + 3] = (byte)(Math.Clamp( 2f - distance, 0f, 1f ) * 255);
			}
		}

		return _dot = Texture.Create( size, size ).WithData( data ).Finish();
	}

	/// <summary>
	/// A dot every two grid steps, fading out as the view zooms out.
	/// </summary>
	void DrawGrid( Painter painter, Rect viewport )
	{
		var alpha = FadeOutBackground ? Math.Clamp( (Zoom - 0.3f) / 0.3f, 0f, 1f ) : 1f;
		if ( alpha <= 0f ) return;

		var step = GridSize * 2f;
		var tile = step * (CanvasToScreen( Vector2.One ) - CanvasToScreen( Vector2.Zero )).x;
		var origin = CanvasToScreen( Vector2.Zero ) - viewport.Position - new Vector2( tile * 0.5f );
		var offset = new Vector2( (origin.x % tile + tile) % tile, (origin.y % tile + tile) % tile );

		painter.Fill = Fill.Image( DotTexture(), Color.White.WithAlpha( 0.1f * alpha ), Length.Pixels( tile ), Length.Pixels( tile ),
			Length.Pixels( offset.x ), Length.Pixels( offset.y ), BackgroundRepeat.Repeat );
		painter.Rect( viewport );
	}
}
