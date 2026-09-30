namespace Sandbox.UI;

/// <summary>
/// A navigable drawing surface. Owns the view transform, cursor-anchored zoom and middle-button
/// panning. Add ordinary panels to Content, or draw directly with Painter. Coordinates returned
/// by CanvasToScreen are local screen pixels, matching Painter and MousePosition.
/// </summary>
[StyleSheet.Inline( "canvaspanel", ".canvaspanel { position: relative; overflow: hidden; pointer-events: all; } .canvas-selection-box { position: absolute; pointer-events: none; z-index: 100; border: 1px solid white; background-color: rgba(255,255,255,0.08); border-radius: 0; }" )]
public partial class CanvasPanel : Panel
{
	public Vector2 ViewMin { get; private set; } = Vector2.Zero;
	public Vector2 ViewMax { get; private set; } = Vector2.One;
	public bool YAxisUp { get; set; }

	/// <summary>
	/// Fit the requested view uniformly, revealing extra space around its centre. Disable for independent graph axes.
	/// </summary>
	public bool PreserveAspectRatio { get; set; } = true;
	public float MinimumViewSpan { get; set; } = 0.000001f;
	public float MaximumViewSpan { get; set; } = 1000000000;
	public bool IsPanning { get; private set; }
	bool _checkFocus;
	protected virtual bool CanNavigate => !Descendants.OfType<Handle>().Any( x => x.IsDragging );
	protected virtual Rect Viewport => new( Vector2.Zero, Box.Rect.Size );
	Panel _content, _contentViewport;
	(Vector2 Min, Vector2 Max, Rect Viewport, float DpiScale, bool YAxisUp, bool PreserveAspectRatio)? _lastContentView;

	/// <summary>
	/// Ordinary panels placed in canvas coordinates. Their CSS transforms, layout, focus and input
	/// work normally. The content has a stable 1000 x 1000 layout size (set its Style to change it),
	/// with visible overflow so panels can also be placed at negative or larger coordinates.
	/// Pan/zoom transform the whole subtree without changing its layout. Created only when used.
	/// </summary>
	public Panel Content
	{
		get
		{
			if ( _content is not null )
				return _content;
			_contentViewport = AddChild<Panel>();
			_contentViewport.AddClass( "canvas-viewport" );
			_contentViewport.Style.Position = PositionMode.Absolute;
			_contentViewport.Style.Overflow = OverflowMode.Hidden;
			_contentViewport.CanDragScroll = false;
			_content = _contentViewport.AddChild<Panel>();
			_content.AddClass( "canvas-content" );
			_content.Style.Position = PositionMode.Absolute;
			_content.Style.Left = 0;
			_content.Style.Top = 0;
			_content.Style.Width = 1000;
			_content.Style.Height = 1000;
			_content.Style.Overflow = OverflowMode.Visible;
			_content.Style.TransformOriginX = 0;
			_content.Style.TransformOriginY = 0;
			_content.CanDragScroll = false;
			UpdateContentTransform();
			return _content;
		}
	}

	void UpdateContentTransform()
	{
		if ( _content is null || _content.IsDeleting || ScaleToScreen <= 0 )
			return;
		var viewport = Viewport;
		if ( viewport.Width <= 0 || viewport.Height <= 0 )
			return;
		var state = (ViewMin, ViewMax, viewport, ScaleToScreen, YAxisUp, PreserveAspectRatio);
		if ( _lastContentView == state )
			return;
		_lastContentView = state;
		_contentViewport.Style.Left = viewport.Left / ScaleToScreen;
		_contentViewport.Style.Top = viewport.Top / ScaleToScreen;
		_contentViewport.Style.Width = viewport.Width / ScaleToScreen;
		_contentViewport.Style.Height = viewport.Height / ScaleToScreen;
		var offset = (CanvasToScreen( Vector2.Zero ) - viewport.Position) / ScaleToScreen;
		var scale = ViewScale / ScaleToScreen;
		if ( YAxisUp )
			scale.y = -scale.y;
		var transform = new PanelTransform();
		transform.AddTranslate( offset.x, offset.y );
		transform.AddScale( new Vector3( scale.x, scale.y, 1 ) );
		_content.Style.Transform = transform;
	}

	Vector2 _panMouse;
	Vector2? _zoomSpan;

	public CanvasPanel()
	{
		AddClass( "canvaspanel" );
		AcceptsFocus = true;
		CanDragScroll = false;
	}

	/// <summary>
	/// Set finite, increasing bounds to fit in the viewport without modifying the content.
	/// </summary>
	public void SetView( Vector2 min, Vector2 max )
	{
		var size = max - min;
		if ( !float.IsFinite( min.x ) || !float.IsFinite( min.y ) || !float.IsFinite( max.x ) || !float.IsFinite( max.y )
			|| !float.IsFinite( size.x ) || !float.IsFinite( size.y ) || size.x <= 0 || size.y <= 0 )
			throw new ArgumentException( "Canvas bounds must be finite and increasing." );
		_zoomSpan = null;
		ViewMin = min;
		ViewMax = max;
		ConstrainView();
		UpdateContentTransform();
	}

	Vector2 ViewScale
	{
		get
		{
			var scale = new Vector2( Math.Max( 1, Viewport.Width ), Math.Max( 1, Viewport.Height ) ) / (ViewMax - ViewMin);
			return PreserveAspectRatio ? new Vector2( Math.Min( scale.x, scale.y ) ) : scale;
		}
	}

	public Vector2 CanvasToScreen( Vector2 point )
	{
		var offset = (point - (ViewMin + ViewMax) * 0.5f) * ViewScale;
		if ( YAxisUp )
			offset.y = -offset.y;
		return Viewport.Position + Viewport.Size * 0.5f + offset;
	}

	public Vector2 ScreenToCanvas( Vector2 point )
	{
		var offset = (point - Viewport.Position - Viewport.Size * 0.5f) / ViewScale;
		if ( YAxisUp )
			offset.y = -offset.y;
		return (ViewMin + ViewMax) * 0.5f + offset;
	}

	public void Pan( Vector2 screenDelta )
	{
		var delta = ScreenToCanvas( Vector2.Zero ) - ScreenToCanvas( screenDelta );
		var zoomSpan = _zoomSpan;
		SetView( ViewMin + delta, ViewMax + delta );
		_zoomSpan = zoomSpan;
	}

	/// <summary>
	/// Scale the visible span while keeping the screen anchor fixed.
	/// </summary>
	public void ZoomAt( Vector2 screenAnchor, float factor )
	{
		if ( !float.IsFinite( factor ) || factor <= 0 )
			return;
		// When just one axis is bounded, remember its unclamped span so reversing
		// the wheel restores the same zoom instead of immediately shrinking that axis.
		bool independentLimit = !PreserveAspectRatio && NavigationBounds.HasValue
			&& ConstrainHorizontalNavigation != ConstrainVerticalNavigation;
		var size = (independentLimit ? _zoomSpan ?? (ViewMax - ViewMin) : ViewMax - ViewMin) * factor;
		if ( !float.IsFinite( size.x ) || !float.IsFinite( size.y )
			|| size.x < MinimumViewSpan || size.x > MaximumViewSpan || size.y < MinimumViewSpan || size.y > MaximumViewSpan )
			return;
		var anchor = ScreenToCanvas( screenAnchor );
		var scale = size / (ViewMax - ViewMin);
		SetView( anchor + (ViewMin - anchor) * scale, anchor + (ViewMax - anchor) * scale );
		if ( independentLimit )
			_zoomSpan = size;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( e.Button == "mouseleft" && BoxSelectionEnabled && CanNavigate && Viewport.IsInside( MousePosition )
			&& (e.Target == this || e.Target == _content || e.Target == _contentViewport) )
		{
			BeginBoxSelection( e.HasShift );
			e.StopPropagation();
			return;
		}
		if ( e.Button != "mousemiddle" || IsBoxSelecting || !CanNavigate || !Viewport.IsInside( MousePosition ) )
		{
			base.OnMouseDown( e );
			return;
		}
		e.StopPropagation();
		Focus();
		IsPanning = true;
		_panMouse = MousePosition;
	}
	protected override void OnMouseMove( MousePanelEvent e )
	{
		if ( IsBoxSelecting )
		{
			UpdateBoxSelection();
			e.StopPropagation();
			return;
		}
		if ( !IsPanning )
		{
			base.OnMouseMove( e );
			return;
		}
		e.StopPropagation();
		Pan( MousePosition - _panMouse );
		_panMouse = MousePosition;
	}
	protected override void OnMouseUp( MousePanelEvent e )
	{
		if ( IsBoxSelecting && e.Button == "mouseleft" )
		{
			UpdateBoxSelection();
			FinishBoxSelection();
			e.StopPropagation();
			return;
		}
		if ( !IsPanning || e.Button != "mousemiddle" )
		{
			base.OnMouseUp( e );
			return;
		}
		e.StopPropagation();
		IsPanning = false;
	}
	protected override void OnBlur( PanelEvent e )
	{
		if ( e.Target == this )
		{
			// Focus requested by the pan can settle after the mouse-down target receives focus.
			_checkFocus = true;
			FinishBoxSelection( true );
		}
		base.OnBlur( e );
	}
	public override void OnMouseWheel( Vector2 value )
	{
		if ( CanNavigate && !IsPanning && !IsBoxSelecting && Viewport.IsInside( MousePosition ) )
			ZoomAt( MousePosition, MathF.Pow( 1.15f, Math.Clamp( value.y != 0 ? value.y : value.x, -5, 5 ) ) );
	}
	public override void Tick()
	{
		base.Tick();
		ConstrainView();
		UpdateContentTransform();
		if ( _checkFocus )
		{
			_checkFocus = false;
			if ( !HasFocus && !Descendants.Any( x => x.HasFocus ) )
			{
				IsPanning = false;
				FinishBoxSelection( true );
			}
		}
		if ( IsPanning && !HasActive )
			IsPanning = false;
		if ( IsBoxSelecting && (!HasActive || !BoxSelectionEnabled) )
			FinishBoxSelection( true );
	}
}
