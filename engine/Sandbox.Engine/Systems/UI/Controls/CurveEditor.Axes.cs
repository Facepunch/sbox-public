using System.Globalization;

namespace Sandbox.UI;

public partial class CurveEditor
{
	/// <summary>
	/// Visible time/value bounds in actual curve units. Changing the view does not edit keys.
	/// </summary>
	public Vector2 ViewMin => ViewUnits( _canvas.ViewMin );
	public Vector2 ViewMax => ViewUnits( _canvas.ViewMax );

	Vector2 ViewUnits( Vector2 point )
	{
		var curve = _curves[0];
		return new Vector2( curve.TimeRange.x, curve.ValueRange.x ) + point * new Vector2( curve.TimeRange.y - curve.TimeRange.x, curve.ValueRange.y - curve.ValueRange.x );
	}

	/// <summary>
	/// Set the visible time/value bounds in actual units.
	/// </summary>
	public void SetViewBounds( Vector2 min, Vector2 max )
	{
		var curve = _curves[0];
		var origin = new Vector2( curve.TimeRange.x, curve.ValueRange.x );
		var span = new Vector2( curve.TimeRange.y - curve.TimeRange.x, curve.ValueRange.y - curve.ValueRange.x );
		_canvas.SetView( (min - origin) / span, (max - origin) / span );
	}

	// Store limits in normalized space so axis edits and undo keep navigation aligned with the curves.
	Rect? _navigationBounds;

	/// <summary>
	/// Navigation bounds in actual time/value units. Only the time limits constrain navigation;
	/// vertical pan and zoom remain free. Bounds rescale with edits to the curve's axis ranges.
	/// </summary>
	public Rect? NavigationBounds
	{
		get => _navigationBounds is { } rect ? new Rect( ViewUnits( rect.Position ), ViewUnits( rect.Position + rect.Size ) - ViewUnits( rect.Position ) ) : null;
		set
		{
			Rect? normalized = null;
			if ( value is { } rect )
			{
				var curve = _curves[0];
				var origin = new Vector2( curve.TimeRange.x, curve.ValueRange.x );
				var span = new Vector2( curve.TimeRange.y - curve.TimeRange.x, curve.ValueRange.y - curve.ValueRange.x );
				normalized = new Rect( (rect.Position - origin) / span, rect.Size / span );
			}
			ApplyNavigationBounds( normalized );
			_navigationBounds = normalized;
		}
	}
	void ApplyNavigationBounds( Rect? bounds ) => _canvas.NavigationBounds = bounds;

	// Match the Widget editor: edit a range endpoint on every curve by the same actual-unit
	// delta, retaining normalized keys. Move the corresponding viewport endpoint with it.
	internal void EditAxisRange( string text, bool horizontal, bool maximum )
	{
		if ( !float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) || !float.IsFinite( value ) )
			return;
		EditAxisRange( value, horizontal, maximum );
	}

	void EditAxisRange( float value, bool horizontal, bool maximum )
	{
		var reference = horizontal ? _curves[0].TimeRange : _curves[0].ValueRange;
		float delta = value - (maximum ? reference.y : reference.x);
		if ( !float.IsFinite( delta ) )
			return;
		var curves = (Curve[])_curves.Clone();
		for ( int i = 0; i < curves.Length; i++ )
		{
			var range = horizontal ? curves[i].TimeRange : curves[i].ValueRange;
			if ( maximum )
				range.y += delta;
			else
				range.x += delta;
			if ( !float.IsFinite( range.x ) || !float.IsFinite( range.y ) || !float.IsFinite( range.y - range.x ) || range.y <= range.x )
				return;
			if ( horizontal )
				curves[i].UpdateTimeRange( range, false );
			else
				curves[i].UpdateValueRange( range, false );
		}
		var min = ViewMin;
		var max = ViewMax;
		if ( horizontal )
		{
			if ( maximum )
				max.x += delta;
			else
				min.x += delta;
		}
		else
		{
			if ( maximum )
				max.y += delta;
			else
				min.y += delta;
		}
		Change( curves );
		// A heavily zoomed view can become inverted when an endpoint shrinks past it.
		if ( float.IsFinite( min.x ) && float.IsFinite( min.y ) && float.IsFinite( max.x ) && float.IsFinite( max.y ) && min.x < max.x && min.y < max.y )
			SetViewBounds( min, max );
		else
			_canvas.SetView( Vector2.Zero, Vector2.One );
	}

	sealed partial class CurveCanvas
	{
		protected override Vector2 AxisMinimum => new( _editor._curves[0].TimeRange.x, _editor._curves[0].ValueRange.x );
		protected override Vector2 AxisMaximum => new( _editor._curves[0].TimeRange.y, _editor._curves[0].ValueRange.y );
		protected override Color AxisColor => _editor.ComputedStyle.FontColor ?? Color.White;
		public override Vector2 CanvasToAxis( Vector2 point ) => _editor.ViewUnits( point );
		public override Vector2 AxisToCanvas( Vector2 point ) => (point - AxisMinimum) / (AxisMaximum - AxisMinimum);
	}
}
