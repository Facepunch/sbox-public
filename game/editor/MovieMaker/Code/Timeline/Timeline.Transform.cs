using Sandbox.MovieMaker;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

public partial class Timeline
{
	private float _pixelsPerSecond = 100f;

	/// <summary>
	/// Invoked when the view pans, scrolls, or changes scale.
	/// </summary>
	public event Action<Rect>? ViewChanged;

	public float PixelsPerSecond
	{
		get => _pixelsPerSecond;
		private set => _pixelsPerSecond = Session.Cookies.PixelsPerSecond = value;
	}

	public Rect VisibleRect
	{
		get
		{
			var screenRect = ScreenRect;
			var topLeft = FromScreen( screenRect.TopLeft );
			var bottomRight = FromScreen( screenRect.BottomRight );

			return ToScene( new Rect( topLeft, bottomRight - topLeft ) );
		}
	}

	public MovieTimeRange VisibleTimeRange
	{
		get
		{
			var visibleRect = VisibleRect;

			return (PixelsToTime( visibleRect.Left ), PixelsToTime( visibleRect.Right ));
		}
	}

	public MovieTime TimeOffset => VisibleTimeRange.Start;

	public MovieTime PixelsToTime( float pixels ) => MovieTime.FromSeconds( PixelsToSeconds( pixels ) );

	public double PixelsToSeconds( float pixels ) => pixels / PixelsPerSecond;

	public float TimeToPixels( MovieTime time ) => TimeToPixels( time.TotalSeconds );
	public float TimeToPixels( double seconds ) => (float)(seconds * PixelsPerSecond);

	private double _zoomOriginTime;
	private readonly SmoothDeltaFloat _smoothZoom = new( 0.3f, 100f );

	public void ZoomSmooth( float v, float x )
	{
		_zoomOriginTime = PixelsToSeconds( x );

		_smoothZoom.Target = (_smoothZoom.Target * MathF.Pow( 2f, v )).Clamp( 1f / 4f, 1024f );
	}

	private void UpdateZoom()
	{
		if ( !_smoothZoom.Update( RealTime.Delta ) )
		{
			return;
		}

		var d = TimeToPixels( _zoomOriginTime );

		PixelsPerSecond = _smoothZoom.Value;

		var nd = TimeToPixels( _zoomOriginTime );

		PanImmediate( d - nd );
	}

	public void PanImmediate( float dx )
	{
		if ( dx == 0 )
			return;

		Translate( new Vector2( dx, 0f ) );
	}

	public void ScrollImmediate( float dy )
	{
		if ( dy == 0 )
			return;

		Translate( new Vector2( 0f, dy ) );
	}

	public void SetView( MovieTime timeOffset, float pixelsPerSecond )
	{
		_pixelsPerSecond = pixelsPerSecond;

		var size = VisibleRect.Size;

		Center = new Vector2( TimeToPixels( timeOffset ) - size.x * 0.5f, Center.y );
	}

	private Rect? _lastVisibleRect;

	private const float LeftRightMargin = 8f;
	private const float TopBottomMargin = 48f;

	private bool UpdateView()
	{
		var width = TimeToPixels( MovieTime.FromSeconds( 60d * 60d * 10d ) ) + LeftRightMargin * 2f;
		var height = Session.TrackList.Height + TopBottomMargin * 2f;

		VerticalScrollbar = height > Height ? ScrollbarMode.On : ScrollbarMode.Off;

		SceneRect = new Rect(
			-LeftRightMargin,
			-TopBottomMargin,
			Math.Max( Width, width ),
			Math.Max( Height, height ) );

		var visibleRect = VisibleRect;

		UpdateBackground( visibleRect );
		UpdateScrubBars( visibleRect );

		if ( visibleRect == _lastVisibleRect ) return false;

		_lastVisibleRect = visibleRect;

		UpdatePlayheadTime( Session.PlayheadTime );
		UpdatePreviewTime( Session.PreviewTime );

		ViewChanged?.Invoke( visibleRect );
		Session.EditMode?.ViewChanged( visibleRect );

		return true;
	}

	private void UpdateBackground( Rect visibleRect )
	{
		_backgroundItem.PrepareGeometryChange();
		_backgroundItem.SceneRect = visibleRect;
		_backgroundItem.Update();

		_gridItem.PrepareGeometryChange();
		_gridItem.SceneRect = visibleRect;
		_gridItem.Update();
	}

	private void UpdateScrubBars( Rect visibleRect )
	{
		ScrubBarTop.PrepareGeometryChange();
		ScrubBarBottom.PrepareGeometryChange();

		ScrubBarTop.Position = visibleRect.TopLeft;
		ScrubBarBottom.Position = visibleRect.BottomLeft - new Vector2( 0f, ScrubBar.Height );

		ScrubBarTop.Width = Width;
		ScrubBarBottom.Width = Width;
	}

	/// <summary>
	/// How much space to leave around the playhead when auto-scrolling.
	/// </summary>
	[FromTheme]
	public static float PlayheadMarginPixels { get; set; } = 128f;

	public void PanToPlayheadTime()
	{
		var range = new MovieTimeRange( Session.PlayheadTime - PixelsToTime( PlayheadMarginPixels ), Session.PlayheadTime + PixelsToTime( PlayheadMarginPixels ) );

		if ( range.Start < VisibleTimeRange.Start )
		{
			PanImmediate( TimeToPixels( VisibleTimeRange.Start - range.Start ) );
		}
		else if ( range.End > VisibleTimeRange.End )
		{
			PanImmediate( TimeToPixels( VisibleTimeRange.End - range.End ) );
		}
	}

	public void ScrollToTrack( TrackView trackView ) => ScrollToTrack( trackView.Track );

	public void ScrollToTrack( IProjectTrack track )
	{
		if ( Tracks.FirstOrDefault( x => x.View.Track.Id == track.Id ) is not { } timelineTrack ) return;

		var trackSceneRect = timelineTrack.GetRealSceneRect();
		var visibleRect = VisibleRect.Shrink( 0f, TopBottomMargin );

		if ( trackSceneRect.Top < visibleRect.Top )
		{
			ScrollImmediate( visibleRect.Top - trackSceneRect.Top );
		}
		else if ( trackSceneRect.Bottom > visibleRect.Bottom )
		{
			ScrollImmediate( visibleRect.Bottom - trackSceneRect.Bottom );
		}
	}

	protected override void OnPaint()
	{
		// I don't think there's a signal we can listen to for a scroll bar
		// being dragged, so let's check during OnPaint so we immediately
		// move things like the scrub bars

		UpdateView();

		base.OnPaint();
	}
}
