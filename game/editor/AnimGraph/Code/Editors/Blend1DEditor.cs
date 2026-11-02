using Editor.AnimGraph.Nodes;

namespace Editor.AnimGraph;

/// <summary>
/// Pannable, zoomable 1D blend axis with draggable points, shown as a page
/// inside the anim graph window. Selected points edit in the properties panel.
/// </summary>
[NodePage( typeof( Blend1DNode ) )]
internal sealed class BlendAxisCanvas : CanvasWidget, INodePage
{
	private Blend1DNode _node;

	internal static BlendAxisCanvas Current { get; private set; }
	internal Blend1DNode Node => _node;

	public int Selected { get; private set; } = -1;
	public Blend1DNode.BlendPoint SelectedPoint => Selected >= 0 && Selected < _node.Points.Count ? _node.Points[Selected] : null;
	public bool SupportsSecondaryView => true;

	private float _gridStep = 1;

	private int _dragIndex = -1;
	private int _hoverIndex = -1;

	protected override bool LockY => true;
	protected override float MaxZoom => 5000;

	// Zoom floor adapts to the points — any value range can always be framed
	// with room to spare, while unit-scale spaces keep a sane limit.
	protected override float MinZoom
	{
		get
		{
			var span = _node.Points.Count > 0
				? MathF.Max( _node.Points.Max( p => p.Value ) - _node.Points.Min( p => p.Value ), 0.5f )
				: 0.5f;

			return MathF.Min( 10, Width * 0.25f / span );
		}
	}

	public BlendAxisCanvas( Widget parent, Blend1DNode node, Window window ) : base( parent, window )
	{
		_node = node;

		MinimumSize = new Vector2( 300, 200 );

		Current = this;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		if ( Current == this )
			Current = null;
	}

	private float AxisY => Height * 0.5f;

	private float ToCanvas( float value ) => ToCanvas( new Vector2( value, 0 ) ).x;
	private float FromCanvas( float x ) => FromCanvas( new Vector2( x, 0 ) ).x;

	protected override void Fit()
	{
		var min = _node.Points.Count > 0 ? _node.Points.Min( p => p.Value ) : -1;
		var max = _node.Points.Count > 0 ? _node.Points.Max( p => p.Value ) : 1;

		FrameView( new Vector2( (min + max) * 0.5f, 0 ), Width / (MathF.Max( max - min, 0.5f ) * 1.4f) );
	}

	private int HitPoint( Vector2 local )
	{
		for ( var i = _node.Points.Count - 1; i >= 0; i-- )
		{
			if ( new Vector2( ToCanvas( _node.Points[i].Value ), AxisY ).Distance( local ) <= 14 )
				return i;
		}

		return -1;
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
		if ( node is not Blend1DNode blend || blend.Identifier != _node.Identifier )
			return false;

		_node = blend;

		if ( Selected >= blend.Points.Count )
			Selected = -1;

		Editor.OnCanvasSelection( _node, SelectedPoint );
		Update();
		return true;
	}

	public void OnGraphEdited() => Update();

	private void Changed()
	{
		_node.Update();
		Editor.MarkDirty();
		Update();
	}

	private void AddPoint( float value )
	{
		Editor.PushGraphUndo( "Add Blend Point" );
		_node.AddPoint( value );
		Changed();
		Editor.PushGraphRedo();
		Select( _node.Points.Count - 1 );
	}

	private void RemovePoint( int index )
	{
		if ( _node.Points.Count <= 2 )
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

		AddPoint( Snap( FromCanvas( e.LocalPosition.x ), true ) );
		e.Accepted = true;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( _dragIndex >= 0 && _dragIndex < _node.Points.Count )
		{
			_node.Points[_dragIndex].Value = Snap( FromCanvas( e.LocalPosition.x ), e.HasCtrl );
			Changed();
			e.Accepted = true;
			return;
		}

		base.OnMouseMove( e );

		if ( IsPanning )
			return;

		var hover = HitPoint( e.LocalPosition );

		if ( hover != _hoverIndex )
		{
			_hoverIndex = hover;
			Cursor = hover >= 0 ? CursorShape.Finger : CursorShape.Arrow;
			Update();
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
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

	private float Snap( float value, bool snap )
	{
		if ( !snap )
			return value;

		var step = _gridStep * 0.25f;
		return MathF.Round( value / step ) * step;
	}

	private void OpenContextMenu( Vector2 local )
	{
		var menu = new Menu( this );
		var hit = HitPoint( local );

		if ( hit >= 0 )
		{
			Select( hit );
			menu.AddOption( "Edit Point", "linear_scale", () => Editor.NavigateInto( SelectedPoint.Graph ) );
			menu.AddSeparator();
			var remove = menu.AddOption( "Remove Point", "delete", () => RemovePoint( hit ) );
			remove.Enabled = _node.Points.Count > 2;
		}
		else
		{
			var value = Snap( FromCanvas( local.x ), true );
			menu.AddOption( "Add Point Here", "add", () => AddPoint( value ) );
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
		PaintAxis();
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

		var min = FromCanvas( 0 );
		var max = FromCanvas( Width );

		Paint.ClearBrush();
		Paint.SetDefaultFont( 7 );

		for ( var x = MathF.Floor( min / _gridStep ) * _gridStep; x <= max; x += _gridStep )
		{
			var sx = ToCanvas( x );
			var zero = MathF.Abs( x ) < _gridStep * 0.5f;

			Paint.SetPen( zero ? Theme.WindowBackground.Lighten( 0.7f ) : Theme.WindowBackground.Lighten( 0.3f ) );
			Paint.DrawLine( new Vector2( sx, 0 ), new Vector2( sx, Height ) );

			Paint.SetPen( Color.White.WithAlpha( zero ? 0.4f : 0.25f ) );
			Paint.DrawText( new Rect( sx - 20, Height - 16, 40, 12 ), x.ToString( "0.##" ) );
		}
	}

	private void PaintAxis()
	{
		var points = _node.Points;
		var duplicates = points.GroupBy( p => p.Value ).Any( g => g.Count() > 1 );
		var primary = duplicates ? Theme.Red : Theme.Green;

		if ( points.Count >= 2 )
		{
			Paint.ClearBrush();
			Paint.SetPen( primary.WithAlpha( 0.7f ), 2 );
			Paint.DrawLine(
				new Vector2( ToCanvas( points.Min( p => p.Value ) ), AxisY ),
				new Vector2( ToCanvas( points.Max( p => p.Value ) ), AxisY ) );
		}

		if ( duplicates )
		{
			Paint.SetPen( Theme.Red );
			Paint.SetDefaultFont( 8, 500 );
			Paint.DrawText( LocalRect.Shrink( 0, 0, 0, 32 ), "Invalid Blend Axis — points can't share the same value", TextFlag.CenterBottom );
		}

		for ( var i = 0; i < points.Count; i++ )
			PaintPoint( new Vector2( ToCanvas( points[i].Value ), AxisY ), _node.PointTitle( i ), primary, i == Selected, i == _hoverIndex );
	}

	private void PaintPoint( Vector2 screen, string title, Color primary, bool selected, bool hovered )
	{
		Paint.ClearPen();
		Paint.SetBrush( Color.Black.WithAlpha( 0.3f ) );
		Paint.DrawCircle( screen + new Vector2( 0, 2 ), 18 );

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
}
