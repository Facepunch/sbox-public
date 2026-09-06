namespace Sandbox.UI;

public partial class Panel
{
	internal ScrollBar ScrollbarY;
	internal ScrollBar ScrollbarX;

	/// <summary>
	/// Creates or destroys the scrollbars, like the ::before and ::after elements. They're ordinary
	/// children, kept after everything else and out of the scrollable extent.
	/// </summary>
	void UpdateScrollbars()
	{
		var style = ComputedStyle;
		if ( style is null ) return;

		var wanted = ScrollBar.Thickness( style.ScrollbarWidth, ScaleToScreen ) > 0;

		BuildScrollbar( wanted && HasScrollY, vertical: true, ref ScrollbarY );
		BuildScrollbar( wanted && HasScrollX, vertical: false, ref ScrollbarX );

		if ( ScrollbarY is null && ScrollbarX is null ) return;

		// Always last, in a fixed order
		var last = _children.Count - 1;
		if ( ScrollbarY.IsValid() ) SetChildIndex( ScrollbarY, last-- );
		if ( ScrollbarX.IsValid() ) SetChildIndex( ScrollbarX, last );
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
	int ScrollbarCount
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
	internal Rect ContentClipRect
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
