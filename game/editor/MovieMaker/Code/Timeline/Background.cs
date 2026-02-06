using Sandbox.MovieMaker;
using Sandbox.Services;

namespace Editor.MovieMaker;

public class BackgroundItem : GraphicsItem
{
	public Timeline Timeline { get; }

	public BackgroundItem( Timeline timeline )
	{
		ZIndex = -10_000;
		Timeline = timeline;
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( Timeline.BackgroundColor );
		Paint.DrawRect( LocalRect );

		if ( Timeline.Session.SequenceTimeRange is { } sequenceRange )
		{
			Paint.SetBrushAndPen( Timeline.OuterColor );
			DrawTimeRangeRect( (MovieTime.Zero, Timeline.Session.Duration) );

			Paint.SetBrushAndPen( Timeline.InnerColor );
			DrawTimeRangeRect( sequenceRange );
		}
		else
		{
			Paint.SetBrushAndPen( Timeline.InnerColor );
			DrawTimeRangeRect( (MovieTime.Zero, Timeline.Session.Duration) );
		}
	}

	private void DrawTimeRangeRect( MovieTimeRange timeRange )
	{
		var startX = FromScene( Timeline.TimeToPixels( timeRange.Start ) ).x;
		var endX = FromScene( Timeline.TimeToPixels( timeRange.End ) ).x;

		Paint.DrawRect( new Rect( new Vector2( startX, LocalRect.Top ), new Vector2( endX - startX, LocalRect.Height ) ) );
	}

	private int _lastState;

	public virtual void Frame()
	{
		var state = HashCode.Combine( Timeline.PixelsPerSecond, Timeline.TimeOffset, Timeline.Session.Duration );

		if ( state != _lastState )
		{
			_lastState = state;
			Update();
		}
	}
}

public class GridItem : GraphicsItem
{
	public Timeline Timeline { get; }

	private readonly GridLines _major;
	private readonly GridLines _minor;

	private const float MajorMargin = 8f;
	private const float MinorMargin = 16f;

	public GridItem( Timeline timeline )
	{
		ZIndex = 500;
		Timeline = timeline;

		_major = new( this ) { Thickness = 2f, Position = new Vector2( 0f, MajorMargin ) };
		_minor = new( this ) { Thickness = 1f, Position = new Vector2( 0f, MinorMargin ) };
	}

	public new void Update()
	{
		_major.PrepareGeometryChange();
		_minor.PrepareGeometryChange();

		_major.Interval = Timeline.TimeToPixels( Timeline.MajorTick.Interval );
		_minor.Interval = Timeline.TimeToPixels( Timeline.MinorTick.Interval );

		_major.Size = Size - new Vector2( 0f, MajorMargin * 2f );
		_minor.Size = Size - new Vector2( 0f, MinorMargin * 2f );

		base.Update();
	}
}

public sealed class GridLines : GraphicsItem
{
	public Color Color { get; set; } = Theme.TextControl.WithAlpha( 0.02f );
	public float Thickness { get; set; } = 2f;
	public float Interval { get; set; } = 16f;

	private int? _pixmapHash;
	private Pixmap _pixmap;

	private const int PixmapHeight = 1;

	private int CalculatePixmapHash() => HashCode.Combine( Color, Thickness, (int)MathF.Round( Interval ) );

	public GridLines( GraphicsItem parent = null ) : base( parent ) { }

	private Pixmap GetPixmap()
	{
		var hash = CalculatePixmapHash();

		if ( _pixmap is { } pixmap && _pixmapHash == hash )
		{
			return pixmap;
		}

		_pixmapHash = hash;

		// Use nearest power of 2 to avoid allocating too often

		var width = Interval.NearestPowerOfTwo();

		if ( _pixmap?.Width != width )
		{
			_pixmap = new Pixmap( width, PixmapHeight );
		}

		_pixmap.Clear( Color.Transparent );

		using ( Paint.ToPixmap( _pixmap ) )
		{
			Paint.SetPen( Color, Thickness );

			// Draw line on both left and right edge so lines more than 1 px wide don't cut off

			Paint.DrawLine( 0, new Vector2( 0, PixmapHeight ) );
			Paint.DrawLine( _pixmap.Width, new Vector2( _pixmap.Width, PixmapHeight ) );
		}

		return _pixmap;
	}

	protected override void OnPaint()
	{
		var pixmap = GetPixmap();
		var offset = ToScene( Position ).x;
		var scale = Interval / pixmap.Width;

		Paint.ClearPen();
		Paint.Translate( new Vector2( -offset, 0f ) );
		Paint.Scale( scale, 1f );
		Paint.SetBrush( pixmap );
		Paint.DrawRect( LocalRect with
		{
			Left = LocalRect.Left + offset / scale,
			Right = LocalRect.Left + LocalRect.Width / scale + offset / scale
		} );
	}
}
