using Editor.AnimGraph.Nodes;

namespace Editor.AnimGraph;

/// <summary>
/// Full-page editor for 2D blend spaces: the blend space canvas beside its own
/// preview scene, driven by dragging the blend handle - independent of the
/// graph preview and its parameters.
/// </summary>
[NodePage( typeof( Blend2DNode ) )]
internal sealed class BlendSpacePage : Widget, INodePage
{
	private readonly BlendSpaceCanvas _canvas;
	private readonly BlendSpacePreview _preview;
	private readonly Splitter _splitter;

	public CanvasWidget ViewCanvas => _canvas;
	public bool SupportsSecondaryView => true;

	public BlendSpacePage( Widget parent, Blend2DNode node, Window window ) : base( parent )
	{
		Layout = Layout.Row();

		_splitter = new Splitter( this );
		_canvas = new BlendSpaceCanvas( _splitter, node, window );
		_preview = new BlendSpacePreview( _splitter, node, window );

		_splitter.AddWidget( _canvas );
		_splitter.AddWidget( _preview );
		_splitter.SetCollapsible( 0, false );
		_splitter.SetCollapsible( 1, false );
		_splitter.SetStretch( 0, 1 );
		_splitter.SetStretch( 1, 1 );

		var savedState = ProjectCookie.Get<string>( "AnimGraph.BlendSpaceSplitter", null );
		if ( !string.IsNullOrEmpty( savedState ) )
			_splitter.RestoreState( savedState );

		Layout.Add( _splitter );

		_canvas.BlendValueChanged += _preview.SetBlendValue;
		_canvas.PointsChanged += _preview.MarkDirty;
	}

	public override void OnDestroyed()
	{
		ProjectCookie.Set( "AnimGraph.BlendSpaceSplitter", _splitter.SaveState() );
		base.OnDestroyed();
	}

	public bool Rebind( Node node )
	{
		if ( !_canvas.Rebind( node ) )
			return false;

		_preview.Rebind( (Blend2DNode)node );
		return true;
	}

	public void OnGraphEdited()
	{
		_canvas.Update();
		_preview.MarkDirty();
	}
}

/// <summary>
/// Pannable, zoomable blend space canvas with draggable points and a draggable
/// blend handle scrubbing the page's preview. Selected points edit in the
/// properties panel.
/// </summary>
internal sealed class BlendSpaceCanvas : CanvasWidget
{
	private Blend2DNode _node;

	internal static BlendSpaceCanvas Current { get; private set; }
	internal Blend2DNode Node => _node;

	public int Selected { get; private set; } = -1;
	public Blend2DNode.BlendPoint SelectedPoint => Selected >= 0 && Selected < _node.Points.Count ? _node.Points[Selected] : null;

	private float _gridStep = 1;

	private int _dragIndex = -1;
	private int _hoverIndex = -1;

	private Vector2 _blendValue;
	private bool _draggingHandle;
	private bool _hoverHandle;

	internal event Action<Vector2> BlendValueChanged;
	internal event Action PointsChanged;

	protected override bool FlipY => true;
	protected override float MaxZoom => 5000;

	// Zoom floor adapts to the points — any value range can always be framed
	// with room to spare, while unit-scale spaces keep a sane limit.
	protected override float MinZoom
	{
		get
		{
			var min = new Vector2( float.MaxValue );
			var max = new Vector2( float.MinValue );

			foreach ( var point in _node.Points )
			{
				min = Vector2.Min( min, point.Position );
				max = Vector2.Max( max, point.Position );
			}

			var span = _node.Points.Count > 0 ? MathF.Max( MathF.Max( max.x - min.x, max.y - min.y ), 0.5f ) : 0.5f;

			return MathF.Min( 10, MathF.Min( Width, Height ) * 0.25f / span );
		}
	}

	public BlendSpaceCanvas( Widget parent, Blend2DNode node, Window window ) : base( parent, window )
	{
		_node = node;

		MinimumSize = new Vector2( 300, 300 );

		Current = this;
	}

	protected override Vector2 SizeHint() => new( 500, 500 );

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		if ( Current == this )
			Current = null;
	}

	protected override void Fit()
	{
		var min = new Vector2( float.MaxValue );
		var max = new Vector2( float.MinValue );

		foreach ( var point in _node.Points )
		{
			min = Vector2.Min( min, point.Position );
			max = Vector2.Max( max, point.Position );
		}

		if ( _node.Points.Count == 0 )
		{
			min = new Vector2( -1 );
			max = new Vector2( 1 );
		}

		var span = Vector2.Max( max - min, new Vector2( 0.5f ) ) * 1.4f;
		FrameView( (min + max) * 0.5f, MathF.Min( Width / span.x, Height / span.y ) );
	}

	private int HitPoint( Vector2 local )
	{
		for ( var i = _node.Points.Count - 1; i >= 0; i-- )
		{
			if ( ToCanvas( _node.Points[i].Position ).Distance( local ) <= 14 )
				return i;
		}

		return -1;
	}

	private bool HitHandle( Vector2 local ) => ToCanvas( _blendValue ).Distance( local ) <= 10;

	private void SetBlendValue( Vector2 value )
	{
		_blendValue = value;
		BlendValueChanged?.Invoke( value );
		Update();
	}

	private void Select( int index )
	{
		if ( Selected == index )
			return;

		Selected = index;
		Editor.OnCanvasSelection( _node, SelectedPoint );
		Update();
	}

	internal void SelectForTest( int index ) => Select( index );

	/// <summary>
	/// Points this canvas at the same node in a rebuilt graph, keeping the
	/// view and selection - no widget churn, no flicker.
	/// </summary>
	public bool Rebind( Node node )
	{
		if ( node is not Blend2DNode blend || blend.Identifier != _node.Identifier )
			return false;

		_node = blend;

		if ( Selected >= blend.Points.Count )
			Selected = -1;

		Editor.OnCanvasSelection( _node, SelectedPoint );
		Update();
		return true;
	}

	private void Changed()
	{
		_node.Update();
		Editor.MarkDirty();
		PointsChanged?.Invoke();
		Update();
	}

	private void AddPoint( Vector2 position )
	{
		Editor.PushGraphUndo( "Add Blend Point" );
		_node.AddPoint( position );
		Changed();
		Editor.PushGraphRedo();
		Select( _node.Points.Count - 1 );
	}

	private void RemovePoint( int index )
	{
		if ( _node.Points.Count <= 3 )
			return;

		Editor.PushGraphUndo( "Remove Blend Point" );
		_node.RemovePoint( _node.Points[index] );
		Changed();
		Editor.PushGraphRedo();
		Select( -1 );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			if ( HitHandle( e.LocalPosition ) )
			{
				_draggingHandle = true;
				e.Accepted = true;
				return;
			}

			var hit = HitPoint( e.LocalPosition );
			var canDrag = hit >= 0 && hit == Selected;
			Select( hit );

			if ( canDrag )
			{
				_dragIndex = hit;
				Editor.PushGraphUndo( "Move Blend Point" );
			}
			else if ( hit < 0 )
			{
				StartPan( e.LocalPosition );
			}

			e.Accepted = true;
			return;
		}

		if ( e.RightMouseButton )
		{
			OpenContextMenu( e.LocalPosition );
			e.Accepted = true;
			return;
		}

		base.OnMousePress( e );
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		if ( !e.LeftMouseButton )
			return;

		var hit = HitPoint( e.LocalPosition );
		if ( hit >= 0 )
		{
			if ( _dragIndex >= 0 )
			{
				_dragIndex = -1;
				Editor.PushGraphRedo();
			}

			Select( hit );
			Editor.NavigateInto( SelectedPoint.Graph );
			e.Accepted = true;
			return;
		}

		if ( !HitHandle( e.LocalPosition ) )
		{
			AddPoint( Snap( FromCanvas( e.LocalPosition ), true ) );
			e.Accepted = true;
		}
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( _draggingHandle )
		{
			SetBlendValue( Snap( FromCanvas( e.LocalPosition ), e.HasCtrl ) );
			e.Accepted = true;
			return;
		}

		if ( _dragIndex >= 0 && _dragIndex < _node.Points.Count )
		{
			_node.Points[_dragIndex].Position = Snap( FromCanvas( e.LocalPosition ), e.HasCtrl );
			Changed();
			e.Accepted = true;
			return;
		}

		base.OnMouseMove( e );

		if ( IsPanning )
			return;

		var hover = HitPoint( e.LocalPosition );
		var hoverHandle = HitHandle( e.LocalPosition );

		if ( hover != _hoverIndex || hoverHandle != _hoverHandle )
		{
			_hoverIndex = hover;
			_hoverHandle = hoverHandle;
			Cursor = hover >= 0 || hoverHandle ? CursorShape.Finger : CursorShape.Arrow;
			Update();
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		_draggingHandle = false;

		if ( _dragIndex >= 0 )
		{
			_dragIndex = -1;
			Editor.PushGraphRedo();
		}

		base.OnMouseReleased( e );
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		switch ( e.Key )
		{
			case KeyCode.Delete when Selected >= 0:
				RemovePoint( Selected );
				break;
			default:
				base.OnKeyPress( e );
				break;
		}
	}

	private Vector2 Snap( Vector2 p, bool snap )
	{
		if ( !snap )
			return p;

		var step = _gridStep * 0.25f;
		return new Vector2( MathF.Round( p.x / step ) * step, MathF.Round( p.y / step ) * step );
	}

	private void OpenContextMenu( Vector2 local )
	{
		var menu = new Menu( this );
		var hit = HitPoint( local );

		if ( hit >= 0 )
		{
			Select( hit );
			menu.AddOption( "Edit Point", "grain", () => Editor.NavigateInto( SelectedPoint.Graph ) );
			menu.AddSeparator();
			var remove = menu.AddOption( "Remove Point", "delete", () => RemovePoint( hit ) );
			remove.Enabled = _node.Points.Count > 3;
		}
		else
		{
			var position = Snap( FromCanvas( local ), true );
			menu.AddOption( "Add Point Here", "add", () => AddPoint( position ) );
		}

		menu.AddSeparator();
		menu.AddOption( "Fit View", "fit_screen", Fit );

		menu.OpenAtCursor();
	}

	protected override void PaintCanvas()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground );
		Paint.DrawRect( LocalRect );

		PaintGrid();
		PaintBlendSpace();
		PaintHint();
	}

	private void PaintHint()
	{
		Paint.SetPen( Color.White.WithAlpha( 0.3f ) );
		Paint.SetDefaultFont( 7 );
		Paint.DrawText( LocalRect.Shrink( 0, 4, 0, 0 ),
			"Double-click to add a point  ·  Drag to move  ·  Ctrl snaps to grid  ·  Right-click for options",
			TextFlag.CenterTop );
	}

	private void PaintGrid()
	{
		// Pick a grid step that keeps lines a comfortable distance apart
		_gridStep = 1;
		while ( _gridStep * Zoom < 40 ) _gridStep *= 2;
		while ( _gridStep * Zoom > 100 ) _gridStep *= 0.5f;

		var min = FromCanvas( new Vector2( 0, Height ) );
		var max = FromCanvas( new Vector2( Width, 0 ) );

		Paint.ClearBrush();
		Paint.SetDefaultFont( 7 );

		for ( var x = MathF.Floor( min.x / _gridStep ) * _gridStep; x <= max.x; x += _gridStep )
		{
			var sx = ToCanvas( new Vector2( x, 0 ) ).x;
			var zero = MathF.Abs( x ) < _gridStep * 0.5f;

			Paint.SetPen( zero ? Theme.WindowBackground.Lighten( 0.7f ) : Theme.WindowBackground.Lighten( 0.3f ) );
			Paint.DrawLine( new Vector2( sx, 0 ), new Vector2( sx, Height ) );

			Paint.SetPen( Color.White.WithAlpha( zero ? 0.4f : 0.25f ) );
			Paint.DrawText( new Rect( sx - 20, Height - 16, 40, 12 ), x.ToString( "0.##" ) );
		}

		for ( var y = MathF.Floor( min.y / _gridStep ) * _gridStep; y <= max.y; y += _gridStep )
		{
			var sy = ToCanvas( new Vector2( 0, y ) ).y;
			var zero = MathF.Abs( y ) < _gridStep * 0.5f;

			Paint.SetPen( zero ? Theme.WindowBackground.Lighten( 0.7f ) : Theme.WindowBackground.Lighten( 0.3f ) );
			Paint.DrawLine( new Vector2( 0, sy ), new Vector2( Width, sy ) );

			Paint.SetPen( Color.White.WithAlpha( zero ? 0.4f : 0.25f ) );
			Paint.DrawText( new Rect( 4, sy - 6, 40, 12 ), y.ToString( "0.##" ), TextFlag.LeftCenter );
		}
	}

	private void PaintBlendSpace()
	{
		var points = _node.Points;
		if ( points.Count < 3 )
			return;

		var positions = points.Select( p => p.Position ).ToList();
		var valid = BlendSpace.Triangulate( positions, out var triangles, out var hull );
		var primary = valid ? Theme.Green : Theme.Red;
		var weights = new float[points.Count];

		if ( valid )
		{
			weights = BlendSpace.CalculateWeights( positions, triangles, hull, _blendValue, out _ );

			Paint.ClearBrush();
			Paint.SetPen( primary.WithAlpha( 0.25f ) );

			for ( var i = 0; i < triangles.Count; i += 3 )
			{
				var a = ToCanvas( points[triangles[i]].Position );
				var b = ToCanvas( points[triangles[i + 1]].Position );
				var c = ToCanvas( points[triangles[i + 2]].Position );

				Paint.DrawLine( a, b );
				Paint.DrawLine( b, c );
				Paint.DrawLine( c, a );
			}

			Paint.SetPen( primary.WithAlpha( 0.7f ), 2 );
			for ( var i = 1; i < hull.Count; i++ )
				Paint.DrawLine( ToCanvas( points[hull[i - 1]].Position ), ToCanvas( points[hull[i]].Position ) );
		}
		else
		{
			Paint.SetPen( Theme.Red );
			Paint.SetDefaultFont( 8, 500 );
			Paint.DrawText( LocalRect, "Invalid Blend Space — points can't be duplicated or collinear" );
		}

		for ( var i = 0; i < points.Count; i++ )
			PaintPoint( ToCanvas( points[i].Position ), _node.PointTitle( i ), primary, i == Selected, i == _hoverIndex, weights[i] );

		if ( valid )
			PaintHandle( ToCanvas( _blendValue ) );
	}

	private void PaintPoint( Vector2 screen, string title, Color primary, bool selected, bool hovered, float weight )
	{
		Paint.ClearPen();
		Paint.SetBrush( Color.Black.WithAlpha( 0.3f ) );
		Paint.DrawCircle( screen + new Vector2( 0, 2 ), 18 );

		// Blend weight ring - grows with this point's contribution
		if ( weight > 0.001f )
		{
			Paint.SetBrush( primary.WithAlpha( 0.15f + weight * 0.2f ) );
			Paint.DrawCircle( screen, 18 + weight * 14 );

			Paint.ClearBrush();
			Paint.SetPen( primary.WithAlpha( 0.4f + weight * 0.6f ), 2 );
			Paint.DrawCircle( screen, 18 + weight * 14 );
			Paint.ClearPen();
		}

		Paint.SetBrush( primary.Darken( hovered || selected ? 0.4f : 0.55f ) );
		Paint.DrawCircle( screen, 18 );

		Paint.ClearBrush();
		Paint.SetPen( selected ? SelectionColor : primary.WithAlpha( hovered ? 0.9f : 0.5f ), selected ? 2 : 1.2f );
		Paint.DrawCircle( screen, 18 );

		Paint.ClearPen();
		Paint.SetBrush( selected ? SelectionColor : primary.Lighten( 0.5f ) );
		Paint.DrawCircle( screen, 7 );

		Paint.SetDefaultFont( 8, 500 );
		Paint.SetPen( Theme.Text.WithAlpha( hovered || selected ? 1f : 0.75f ) );
		Paint.DrawText( new Rect( screen + new Vector2( -60, 16 ), new Vector2( 120, 16 ) ), title );
	}

	private void PaintHandle( Vector2 screen )
	{
		var color = Theme.Red.Lighten( 0.2f );

		Paint.ClearPen();
		Paint.SetBrush( Color.Black.WithAlpha( 0.3f ) );
		Paint.DrawCircle( screen + new Vector2( 0, 1 ), 8 );

		Paint.SetBrush( _draggingHandle ? color.Darken( 0.2f ) : color );
		Paint.DrawCircle( screen, _hoverHandle || _draggingHandle ? 8 : 7 );

		Paint.ClearBrush();
		Paint.SetPen( Color.White.WithAlpha( 0.9f ), 1.5f );
		Paint.DrawCircle( screen, _hoverHandle || _draggingHandle ? 8 : 7 );
	}
}

/// <summary>
/// Isolated preview beside the blend space canvas - plays just this blend
/// space as a tiny compiled graph, scrubbed by the canvas blend handle.
/// Independent of the graph preview and its parameters.
/// </summary>
internal sealed class BlendSpacePreview : PreviewSceneWidget
{
	private readonly Window _window;
	private Blend2DNode _node;

	private bool _dirty = true;
	private RealTimeSince _sinceDirty;
	private Vector2 _blendValue;
	private Label _hint;
	private Graph _previewGraph;

	public BlendSpacePreview( Widget parent, Blend2DNode node, Window window ) : base( parent, "Blend Space Preview" )
	{
		_window = window;
		_node = node;

		MinimumSize = new Vector2( 200, 200 );

		Toolbar.Add( new PreviewOptions( this, "blendspace" ).CreateBar( this ), 1 );

		_hint = new Label( "Connect an animation clip to every point to preview", this ) { Alignment = TextFlag.Center, WordWrap = true };
		_hint.Visible = false;
		Layout.Add( _hint );
	}

	protected override Vector2 SizeHint() => new( 500, 500 );

	public void Rebind( Blend2DNode node )
	{
		_node = node;
		MarkDirty();
	}

	public void MarkDirty()
	{
		_dirty = true;
		_sinceDirty = 0;
	}

	public void SetBlendValue( Vector2 value )
	{
		_blendValue = value;
		ApplyBlendValue();
	}

	private void ApplyBlendValue()
	{
		if ( !Renderer.IsValid() )
			return;

		Renderer.Animation.Parameters.Set( "x", _blendValue.x, persist: true );
		Renderer.Animation.Parameters.Set( "y", _blendValue.y, persist: true );
	}

	private void Rebuild()
	{
		_dirty = false;

		SetModel( _window.Graph?.TargetModel );

		ClearPreviewGraph();

		if ( Renderer is null )
			return;

		// Every point needs a clip - the blend space previews in isolation
		var clips = _node.Points
			.Select( point =>
			{
				var output = point.Graph?.Nodes.OfType<PoseResultNode>().FirstOrDefault();
				return (point.Position, Clip: output?.GetConnectedNode( output.Pose ) as AnimationClipNode);
			} )
			.Where( e => !string.IsNullOrWhiteSpace( e.Clip?.AnimationName ) )
			.ToList();

		if ( clips.Count < 3 || clips.Count != _node.Points.Count )
		{
			_hint.Visible = true;
			Renderer.UseAnimGraph = false;
			return;
		}

		_hint.Visible = false;

		var builder = Graph.Builder;
		builder.Skeleton = _window.Graph?.TargetSkeleton;
		var blend = builder.BlendSpace2D( builder.FloatParameter( "x" ), builder.FloatParameter( "y" ) );

		foreach ( var (position, clip) in clips )
			blend.Add( position, builder.Clip( clip.AnimationName, new GraphBuilder.ClipSettings { Looping = clip.Loop } ) );

		_previewGraph = builder.Create( blend, "blendspace_preview" );

		Renderer.UseAnimGraph = _previewGraph is not null;
		Renderer.Animation.Graph = _previewGraph;

		ApplyBlendValue();
	}

	/// <summary>
	/// The preview graph is built here rather than loaded, so nothing else can be holding it
	/// and each rebuild has to hand the old one back.
	/// </summary>
	private void ClearPreviewGraph()
	{
		if ( _previewGraph is null )
			return;

		if ( Renderer.IsValid() )
			Renderer.Animation.Graph = null;

		_previewGraph.Dispose();
		_previewGraph = null;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		ClearPreviewGraph();
	}

	protected override void OnScenePreFrame()
	{
		if ( _dirty && _sinceDirty > 0.2f )
			Rebuild();

		ApplyPreviewSettings( _window.Graph );
	}
}
