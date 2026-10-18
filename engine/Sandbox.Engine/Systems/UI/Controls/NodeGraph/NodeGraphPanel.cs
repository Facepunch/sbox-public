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
	/// The graph shown. Setting it rebuilds the view.
	/// </summary>
	public IGraph Graph
	{
		get => _graph;
		set
		{
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

		BoxSelectionStarted += additive =>
		{
			_boxBase = additive ? SelectedNodes.ToHashSet() : [];
			if ( !additive ) DeselectConnections();
		};
		BoxSelectionChanged += SelectInBox;
		BoxSelectionFinished += _ => _boxBase = null;
		OnTooltip = BuildConnectionTooltip;
	}

	HashSet<NodePanel> _boxBase;

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
	/// The panel for <paramref name="node"/>.
	/// </summary>
	protected virtual NodePanel CreateNodePanel( INode node, bool isPreview = false ) => new( this, node, isPreview );

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
	/// Remove a node, and its wires, from the graph.
	/// </summary>
	public void RemoveNode( NodePanel node )
	{
		if ( node is null || !_nodes.Remove( node ) ) return;

		OnNodeRemoving( node );
		Graph?.RemoveNode( node.Node );
		node.Delete( true );

		if ( node.Selected ) SelectionChanged?.Invoke();
	}

	partial void OnNodeRemoving( NodePanel node );

	/// <summary>
	/// Rebuild every node panel and wire from <see cref="Graph"/>.
	/// </summary>
	public virtual void RebuildFromGraph()
	{
		var hadSelection = _nodes.Any( x => x.Selected );

		foreach ( var node in _nodes )
		{
			node.Delete( true );
		}

		_nodes.Clear();

		foreach ( var node in Graph?.Nodes ?? [] )
		{
			ShowNode( node );
		}

		OnRebuilt();

		if ( hadSelection ) SelectionChanged?.Invoke();
	}

	partial void OnRebuilt();

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

	public void SelectAll()
	{
		foreach ( var node in _nodes ) node.Selected = true;
		SelectionChanged?.Invoke();
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
	/// Remove the selected wires, and every selected node that can be removed.
	/// </summary>
	public void DeleteSelection()
	{
		OnDeleteSelection();

		foreach ( var node in SelectedNodes.Where( x => x.Node.CanRemove ).ToArray() )
		{
			RemoveNode( node );
		}
	}

	partial void OnDeleteSelection();

	void SelectInBox( Rect box )
	{
		foreach ( var node in _nodes )
		{
			node.Selected = _boxBase.Contains( node ) || BoxSelects( node, box );
		}

		SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Whether a box selection over <paramref name="box"/> takes <paramref name="node"/>. Any overlap does by default.
	/// </summary>
	protected virtual bool BoxSelects( NodePanel node, Rect box ) => new Rect( node.Position, node.Size ).Overlaps( box );

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Button == "delete" && !IsBoxSelecting )
		{
			DeleteSelection();
			e.StopPropagation = true;
			return;
		}

		base.OnButtonTyped( e );
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
		}

		base.Tick();
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
		DrawConnections( painter );
	}

	partial void DrawConnections( Painter painter );

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
