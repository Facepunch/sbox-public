using System;

namespace Editor.Widgets;

public class VideoGallery : Widget
{
	static readonly (string Label, string Url, bool Muted)[] s_testVideos =
	[
		( "AV1 + Opus (MP4)",                                         "https://files.facepunch.com/lolleko/2026/April/17_20-18-AntiquewhiteBedlingtonterrier.mp4",       false ),
		( "VP9 + Opus (WebM)",                                        "https://files.facepunch.com/lolleko/2026/April/17_18-19-AgreeableZebradove.webm",                 true  ),
		( "MP3: The s&box Song by Mungus (Gone but not forgotten)", "https://files.facepunch.com/lolleko/2026/April/18_17-01-ThriftyAsiaticgreaterfreshwaterclam.mp3",   true ),
		( "WebP (animated)",                                          "https://files.facepunch.com/lolleko/2026/April/18_12-02-EmotionalZooplankton.webp",               true  ),
	];

	public VideoGallery( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 8;
		Layout.Margin = 8;

		foreach ( var (label, url, muted) in s_testVideos )
		{
			Layout.Add( new Label( $"{label}  —  {url}" ) { ToolTip = url } );

			var video = new VideoPlayerWidget( this, url ) { Muted = muted };
			video.MinimumSize = new Vector2( 0, 220 );
			Layout.Add( video, 1 );
		}
	}

	[WidgetGallery]
	[Title( "VideoPlayer" )]
	[Icon( "movie" )]
	internal static Widget WidgetGallery() => new VideoGallery( null );
}

/// <summary>
/// A widget that uses a pixmap to display a video.
/// </summary>
public class VideoWidget : Widget
{
	/// <summary>
	/// Access to the video player to control playback.
	/// </summary>
	public VideoPlayer Player { get; private set; }

	private Pixmap background;

	public VideoWidget( Widget parent, string url ) : base( parent )
	{
		MinimumSize = 50;

		Player = new VideoPlayer
		{
			Repeat = true,
			OnTextureData = OnTextureData
		};

		if ( !string.IsNullOrWhiteSpace( url ) )
		{
			Player.Play( url );
		}
	}

	private void OnTextureData( ReadOnlySpan<byte> span, Vector2 size )
	{
		if ( background == null || background.Size != size )
		{
			background = new Pixmap( size );
		}

		background.UpdateFromPixels( span, size, ImageFormat.RGBA8888 );
		Update();
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Color.Black );
		Paint.DrawRect( LocalRect );

		if ( background == null )
			return;

		var textureSize = background.Size;
		var viewportSize = Size;
		var scaleW = viewportSize.x / textureSize.x;
		var scaleH = viewportSize.y / textureSize.y;
		var scale = Math.Min( scaleW, scaleH );
		var newSize = new Vector2( textureSize.x * scale, textureSize.y * scale );
		var rect = new Rect( (viewportSize.x - newSize.x) / 2, (viewportSize.y - newSize.y) / 2, newSize.x, newSize.y );

		Paint.Draw( rect, background );
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		Player?.Dispose();
		Player = null;
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		Player?.Present();
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( e.LeftMouseButton )
		{
			Player?.TogglePause();
		}
	}
}

/// <summary>
/// Wraps <see cref="VideoWidget"/> and adds play/pause, mute, seek and time controls.
/// </summary>
public class VideoPlayerWidget : Widget
{
	public bool Muted
	{
		get => _display.Player.Muted;
		set => _display.Player.Muted = value;
	}

	private readonly VideoWidget _display;
	private readonly FloatSlider _seekSlider;
	private readonly Button _playPauseBtn;
	private readonly Button _muteBtn;
	private readonly Label _timeLabel;
	private bool _userSeeking;

	public VideoPlayerWidget( Widget parent, string url ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 2;

		_display = new VideoWidget( this, url );
		Layout.Add( _display, 1 );

		var controls = new Widget( this );
		controls.Layout = Layout.Row();
		controls.Layout.Spacing = 4;
		controls.Layout.Margin = new Margin( 2, 2, 2, 2 );

		_playPauseBtn = new Button( "⏸", controls );
		_playPauseBtn.Clicked = () =>
		{
			_display.Player?.TogglePause();
			UpdateControls();
		};
		controls.Layout.Add( _playPauseBtn );

		_muteBtn = new Button( "🔊", controls );
		_muteBtn.Clicked = () =>
		{
			_display.Player.Muted = !_display.Player.Muted;
			UpdateControls();
		};
		controls.Layout.Add( _muteBtn );

		_seekSlider = new FloatSlider( controls );
		_seekSlider.Minimum = 0;
		_seekSlider.Maximum = 1;
		_seekSlider.Value = 0;
		_seekSlider.OnValueEdited = () =>
		{
			_userSeeking = true;
			_display.Player?.Seek( _seekSlider.Value );
			_userSeeking = false;
		};
		controls.Layout.Add( _seekSlider, 1 );

		_timeLabel = new Label( "0:00 / 0:00", controls );
		_timeLabel.MinimumWidth = 90;
		controls.Layout.Add( _timeLabel );

		Layout.Add( controls );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		UpdateControls();
	}

	private void UpdateControls()
	{
		var p = _display.Player;
		if ( p == null ) return;

		_playPauseBtn.Text = p.IsPaused ? "▶" : "⏸";
		_muteBtn.Text = p.Muted ? "🔇" : "🔊";

		if ( !_userSeeking && p.Duration > 0 )
			_seekSlider.Value = (float)(p.PlaybackTime / p.Duration);

		static string Fmt( double s ) => $"{(int)s / 60}:{(int)s % 60:D2}";
		_timeLabel.Text = $"{Fmt( p.PlaybackTime )} / {Fmt( p.Duration )}";
	}
}
