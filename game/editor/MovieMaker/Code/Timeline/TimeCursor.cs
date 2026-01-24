using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

public class TimeCursor : GraphicsItem, ISnapSource
{
	private readonly bool _snap;
	private MovieTime _value;

	public Timeline Timeline { get; }
	public Color Color { get; }
	public float LabelFadeTime { get; set; }

	public RealTimeSince ShowLabelTime { get; set; }

	private readonly GraphicsItem _timeLabel;
	private readonly GraphicsItem _frameLabel;

	public MovieTime Value
	{
		get => _value;
		set
		{
			if ( _value != value )
			{
				ShowLabelTime = 0f;
			}

			_value = value;
			UpdatePosition();
		}
	}

	public TimeCursor( Timeline timeline, Color color, bool snap )
	{
		Timeline = timeline;
		Color = color;
		_snap = snap;

		ZIndex = 20_000;
		HandlePosition = new Vector2( 0.5f, 0 );

		_timeLabel = new TimeLabel( this ) { Position = new Vector2( 8f, 0f ) };
		_frameLabel = new TimeLabel( this ) { Position = new Vector2( 8f, Height ), IsFrameLabel = true };
	}

	protected override void OnPaint()
	{
		Paint.SetPen( Color.WithAlpha( 0.5f ) );
		Paint.DrawLine( new Vector2( 0f, 12f ), new Vector2( 0, Height - 12f ) );
		Paint.SetBrushAndPen( Color );

		PaintExtensions.PaintBookmarkDown( Width * 0.5f, 12f, 4, 4, 12 );
		PaintExtensions.PaintBookmarkUp( Width * 0.5f, Height - 12f, 4, 4, 12 );
	}

	public void UpdatePosition()
	{
		PrepareGeometryChange();

		Position = new Vector2( Timeline.TimeToPixels( Value ), Timeline.VisibleRect.Top + 12f );
		Size = new Vector2( 1, Timeline.VisibleRect.Height - 24f );

		_frameLabel.Position = new Vector2( 8f, Height );
	}

	public IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary ) => _snap ? [_value] : [];
}

file sealed class TimeLabel : GraphicsItem
{
	public new TimeCursor Parent { get; }
	public bool IsFrameLabel { get; init; }

	public TimeLabel( TimeCursor parent )
		: base( parent )
	{
		Parent = parent;

		Size = new Vector2( 64f, 20f );
		HandlePosition = new Vector2( 0f, 0.5f );

		ZIndex = 20_100;
	}

	protected override void OnPaint()
	{
		var text = IsFrameLabel
			? Parent.Value.GetFrameIndex( Parent.Timeline.Session.FrameRate ).ToString( "0000" )
			: Parent.Value.ToString();

		var size = Paint.MeasureText( text );

		var alpha = !(Parent.LabelFadeTime <= 0f)
			? 1f - Math.Clamp( Parent.ShowLabelTime / Parent.LabelFadeTime * 4f - 3f, 0f, 1f )
			: 1f;

		Paint.SetBrushAndPen( Theme.ControlBackground.WithAlpha( 0.75f * alpha ) );
		Paint.DrawRect( new Rect( LocalRect.Left, LocalRect.Top, size.x + 16f, LocalRect.Height ), 3f );

		Paint.ClearBrush();
		Paint.SetPen( Parent.Color.WithAlpha( alpha ) );

		Paint.DrawText( LocalRect.Shrink( 8f, 0f, 0f, 0f ), text, TextFlag.LeftCenter );
	}
}
