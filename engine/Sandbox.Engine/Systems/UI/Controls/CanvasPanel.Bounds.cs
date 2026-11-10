namespace Sandbox.UI;

public partial class CanvasPanel
{
	Rect? _navigationBounds;

	/// <summary>
	/// Apply the horizontal limits of NavigationBounds. Enabled by default.
	/// </summary>
	public bool ConstrainHorizontalNavigation { get; set; } = true;

	/// <summary>
	/// Apply the vertical limits of NavigationBounds. Disable to pan and zoom freely above and below them.
	/// </summary>
	public bool ConstrainVerticalNavigation { get; set; } = true;

	/// <summary>
	/// Optional limits in canvas coordinates. Enabled axes stay inside this rectangle when panning, zooming or resizing.
	/// </summary>
	public Rect? NavigationBounds
	{
		get => _navigationBounds;
		set
		{
			if ( value is { } rect && (!float.IsFinite( rect.Left ) || !float.IsFinite( rect.Top ) || !float.IsFinite( rect.Right ) || !float.IsFinite( rect.Bottom )
				|| !float.IsFinite( rect.Width ) || !float.IsFinite( rect.Height ) || rect.Width <= 0 || rect.Height <= 0) )
				throw new ArgumentException( "Navigation bounds must be finite and have positive size.", nameof( value ) );
			if ( _navigationBounds != value )
				_zoomSpan = null;
			_navigationBounds = value;
			ConstrainView();
			UpdateContentTransform();
		}
	}

	/// <summary>
	/// Frame bounds in canvas coordinates with padding on each side, expressed as a fraction
	/// of their size. Minimum padding is in canvas units. Navigation limits take precedence.
	/// </summary>
	public void FitBounds( Rect bounds, Vector2 padding = default, Vector2 minimumPadding = default )
	{
		if ( !float.IsFinite( bounds.Left ) || !float.IsFinite( bounds.Top ) || !float.IsFinite( bounds.Right ) || !float.IsFinite( bounds.Bottom )
			|| bounds.Width < 0 || bounds.Height < 0 )
			throw new ArgumentException( "Fit bounds must be finite and have non-negative size.", nameof( bounds ) );
		if ( !float.IsFinite( padding.x ) || !float.IsFinite( padding.y ) || padding.x < 0 || padding.y < 0 )
			throw new ArgumentException( "Padding must be finite and non-negative.", nameof( padding ) );
		if ( !float.IsFinite( minimumPadding.x ) || !float.IsFinite( minimumPadding.y ) || minimumPadding.x < 0 || minimumPadding.y < 0 )
			throw new ArgumentException( "Minimum padding must be finite and non-negative.", nameof( minimumPadding ) );
		var margin = Vector2.Max( bounds.Size * padding, minimumPadding );
		var center = bounds.Position + bounds.Size * 0.5f;
		// A point far from zero still needs a span large enough to survive float rounding.
		var minimumSpan = Vector2.Max( new Vector2( MinimumViewSpan ), new Vector2( MathF.Abs( center.x ), MathF.Abs( center.y ) ) * 0.000001f );
		var size = Vector2.Max( bounds.Size + margin * 2, minimumSpan );
		size = Vector2.Min( size, new Vector2( MaximumViewSpan ) );
		SetView( center - size * 0.5f, center + size * 0.5f );
	}

	void ConstrainView()
	{
		if ( _navigationBounds is not { } bounds )
			return;
		var span = ViewMax - ViewMin;
		var center = ViewMin + span * 0.5f;
		if ( PreserveAspectRatio && Viewport.Width > 0 && Viewport.Height > 0 )
		{
			// Account for the extra world space exposed by uniform scaling, then cap the
			// zoom-out level using whichever edge is reached first.
			var scale = Viewport.Size / span;
			span = Viewport.Size / Math.Min( scale.x, scale.y );
			float horizontalLimit = ConstrainHorizontalNavigation ? bounds.Width / span.x : 1;
			float verticalLimit = ConstrainVerticalNavigation ? bounds.Height / span.y : 1;
			span *= Math.Min( 1, Math.Min( horizontalLimit, verticalLimit ) );
		}
		// Cap again after uniform scaling to account for floating-point rounding at the boundary.
		if ( ConstrainHorizontalNavigation )
			span.x = Math.Min( span.x, bounds.Width );
		if ( ConstrainVerticalNavigation )
			span.y = Math.Min( span.y, bounds.Height );
		var min = center - span * 0.5f;
		if ( ConstrainHorizontalNavigation )
			min.x = (float)Math.Clamp( (double)min.x, bounds.Left, Math.Max( bounds.Left, (double)bounds.Right - span.x ) );
		if ( ConstrainVerticalNavigation )
			min.y = (float)Math.Clamp( (double)min.y, bounds.Top, Math.Max( bounds.Top, (double)bounds.Bottom - span.y ) );
		ViewMin = min;
		ViewMax = min + span;
		if ( ConstrainHorizontalNavigation )
			ViewMax = new Vector2( Math.Min( ViewMax.x, bounds.Right ), ViewMax.y );
		if ( ConstrainVerticalNavigation )
			ViewMax = new Vector2( ViewMax.x, Math.Min( ViewMax.y, bounds.Bottom ) );
	}
}
