namespace Sandbox.UI;

public partial class Panel
{
	ScrollBar _scrollbarY;
	ScrollBar _scrollbarX;

	// Fixed bars are handled by the root's overlay pass. Detached or deleted bars may still have a field here until the next tick.
	ScrollBar GetScrollbarOverlay( ScrollBar bar ) => bar.IsValid() && bar.Parent == this && !bar.IsFixed ? bar : null;

	/// <summary>
	/// Called by RenderChildren after ordinary child content to draw attached, non-fixed scrollbar overlays.
	/// Clips to the panel's padding box and draws scrollbar shadows before their bodies.
	/// </summary>
	void RenderScrollbars( Painter painter, ref RootPanel.FrameStats stats )
	{
		var horizontal = GetScrollbarOverlay( _scrollbarX );
		var vertical = GetScrollbarOverlay( _scrollbarY );
		if ( horizontal is null && vertical is null ) return;

		using ( ClipChildren( painter, Box.ClipRect ) )
		{
			horizontal?.RenderShadow( painter );
			vertical?.RenderShadow( painter );
			horizontal?.Render( painter, ref stats );
			vertical?.Render( painter, ref stats );
		}
	}

	internal Panel FindScrollbarAt( Vector2 point, bool visibleOnly, bool needPointerEvents, Func<Panel, bool> match = null )
	{
		// Pick in reverse draw order, before content regardless of its z-index.
		return GetScrollbarOverlay( _scrollbarY )?.FindVisualPanelAt( point, visibleOnly, needPointerEvents, match )
			?? GetScrollbarOverlay( _scrollbarX )?.FindVisualPanelAt( point, visibleOnly, needPointerEvents, match );
	}

	void FinalLayoutScrollbars( Vector2 offset )
	{
		GetScrollbarOverlay( _scrollbarX )?.FinalLayout( offset );
		GetScrollbarOverlay( _scrollbarY )?.FinalLayout( offset );
	}

	/// <summary>
	/// Creates or destroys the scrollbars, like the ::before and ::after elements. They're ordinary
	/// children, kept after everything else and out of the scrollable extent.
	/// </summary>
	void UpdateScrollbars()
	{
		var style = ComputedStyle;
		if ( style is null ) return;

		var wanted = ScrollBar.Thickness( style.ScrollbarWidth, ScaleToScreen ) > 0;

		BuildScrollbar( wanted && HasScrollY, vertical: true, ref _scrollbarY );
		BuildScrollbar( wanted && HasScrollX, vertical: false, ref _scrollbarX );

		if ( _scrollbarY is null && _scrollbarX is null ) return;

		// Always last, in a fixed order
		var last = _children.Count - 1;
		if ( _scrollbarY.IsValid() ) SetChildIndex( _scrollbarY, last-- );
		if ( _scrollbarX.IsValid() ) SetChildIndex( _scrollbarX, last );
	}

	void BuildScrollbar( bool shouldExist, bool vertical, ref ScrollBar bar )
	{
		if ( !shouldExist )
		{
			if ( bar is not null )
			{
				bar.Delete();
				bar = null;
			}

			return;
		}

		if ( !bar.IsValid() )
		{
			bar = new ScrollBar( vertical );
			AddChild( bar );
		}
	}

	/// <summary>
	/// How many scrollbars sit at the end of the child list
	/// </summary>
	internal int ScrollbarCount
	{
		get
		{
			if ( _children is null ) return 0;

			int count = 0;
			for ( int i = _children.Count - 1; i >= 0 && _children[i] is ScrollBar; i-- ) count++;

			return count;
		}
	}

	int LastContentChildIndex => _children.Count - 1 - ScrollbarCount;

	/// <summary>
	/// The clip rect less any scrollbar gutter, so content doesn't show through under the bar
	/// </summary>
	public Rect ContentClipRect
	{
		get
		{
			var gutter = LayoutTree?.Gutter ?? default;
			if ( gutter.Left == 0 && gutter.Right == 0 ) return Box.ClipRect;

			return Box.ClipRect.Shrink( gutter.Left, 0, gutter.Right, 0 );
		}
	}

	/// <summary>
	/// The space <c>scrollbar-gutter</c> reserves, in screen pixels. Only for the vertical bar, like the web.
	/// </summary>
	Margin ScrollbarGutter
	{
		get
		{
			var style = ComputedStyle;
			if ( style is null ) return default;
			if ( style.ScrollbarGutter is null or UI.ScrollbarGutter.Auto ) return default;
			if ( style.Overflow != OverflowMode.Scroll ) return default;

			var thickness = ScrollBar.Thickness( style.ScrollbarWidth, ScaleToScreen );
			if ( thickness <= 0 ) return default;

			var left = style.ScrollbarGutter == UI.ScrollbarGutter.StableBothEdges ? thickness : 0;
			return new Margin( left, 0, thickness, 0 );
		}
	}
}
