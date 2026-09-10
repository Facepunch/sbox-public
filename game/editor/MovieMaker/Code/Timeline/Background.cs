using Sandbox.MovieMaker;

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

	private PixmapKey? _pixmapKey;
	private Pixmap _pixmap;

	private const int PixmapHeight = 1;

	private readonly record struct PixmapKey( Color Color, float Thickness, float Interval, int Width );

	public GridLines( GraphicsItem parent = null ) : base( parent ) { }

	private Pixmap GetPixmap()
	{
		var pixmapWidth = Math.Max( (int)Math.Ceiling( Width * 1.25f ), 1 );
		var key = new PixmapKey( Color, Thickness, Interval, pixmapWidth );

		if ( _pixmap is { } pixmap && _pixmapKey == key )
		{
			return pixmap;
		}

		_pixmapKey = key;

		if ( _pixmap?.Width != key.Width )
		{
			_pixmap = new Pixmap( key.Width, PixmapHeight );
		}

		_pixmap.Clear( Color.Transparent );

		using ( Paint.ToPixmap( _pixmap ) )
		{
			Paint.ClearBrush();
			Paint.SetPen( key.Color, key.Thickness );

			for ( var x = 0f; x < _pixmap.Width + key.Interval; x += key.Interval )
			{
				Paint.DrawLine( new Vector2( x, 0f ), new Vector2( x, PixmapHeight ) );
			}
		}

		return _pixmap;
	}

	protected override void OnPaint()
	{
		var pixmap = GetPixmap();
		var offset = ToScene( Position ).x;

		offset -= MathF.Floor( offset / Interval ) * Interval;

		var localRect = LocalRect;

		localRect.Left += offset;
		localRect.Right += offset;

		Paint.ClearPen();
		Paint.Translate( new Vector2( -offset, 0f ) );
		Paint.SetBrush( pixmap );
		Paint.DrawRect( localRect );
	}
}
