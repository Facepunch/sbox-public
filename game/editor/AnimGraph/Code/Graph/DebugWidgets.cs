namespace Editor.AnimGraph;

/// <summary>
/// A self-painting widget for a node's Properties dock Debug section - repaints itself every
/// frame so live content (e.g. a blend preview) stays current. Override <see cref="PaintContent"/>.
/// </summary>
public abstract class LiveCanvas : Widget
{
	private readonly bool _square;
	private readonly float _maxHeight;

	protected LiveCanvas( Widget parent, float height, bool square = false ) : base( parent )
	{
		_square = square;
		_maxHeight = height;
		FixedHeight = height;
	}

	protected abstract void PaintContent( Rect rect );

	[EditorEvent.Frame]
	private void Think()
	{
		if ( !Visible )
			return;

		if ( _square )
			FixedHeight = MathF.Min( Width, _maxHeight );

		Update();
	}

	protected override void OnPaint() => PaintContent( LocalRect );
}

/// <summary>
/// Paints a small readout box beside a live debug marker, kept inside the given area.
/// Shared by preview nodes (e.g. blend spaces) to annotate their live marker.
/// </summary>
public static class DebugMarker
{
	public static void PaintReadout( Rect area, Vector2 marker, string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return;

		Paint.SetDefaultFont( 8 );
		var size = Paint.MeasureText( text );
		var pos = new Vector2(
			MathF.Min( marker.x + 10.0f, area.Right - size.x - 4.0f ),
			Math.Clamp( marker.y - size.y * 0.5f, area.Top + 2.0f, area.Bottom - size.y - 2.0f ) );

		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground.WithAlpha( 0.85f ) );
		Paint.DrawRect( new Rect( pos, size ).Grow( 4.0f, 2.0f ), 3.0f );

		Paint.SetPen( Theme.Text );
		Paint.DrawText( new Rect( pos, size ), text, TextFlag.LeftTop );
	}
}
