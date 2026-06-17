using Sandbox.Internal;
using Sandbox.MovieMaker;
using Sandbox.UI;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static Sandbox.VideoWriter;

namespace Editor.MovieMaker;

#nullable enable

public sealed class VideoExportWindow : BaseWindow
{
	private const float SlowPreviewDelaySeconds = 0.5f;

	public static VideoExportWindow Show( Session session, MovieTimeRange? timeRange )
	{
		var window = new VideoExportWindow( session );

		window.Show();

		return window;
	}

	public Session Session { get; }

	public VideoExportConfig Config { get; }

	public MovieTime PreviewTime { get; set; }
	public MovieTime RenderStartTime { get; private set; }
	public MovieTime LastRenderedTime { get; private set; }
	public Pixmap? LastRenderedFrame { get; private set; }

	private CancellationTokenSource? _cts;
	private Task? _exportTask;

	private MovieTimeRange _oldTimeRange;
	private float _slowPreviewTime;

	private Pixmap? _previewPixmap;
	private Pixmap? _renderPixmap;

	private readonly VideoExportPreview _preview;
	private readonly VideoExportTimeline _timeline;

	private readonly Button _exportButton;
	private readonly Button _cancelButton;

	private bool IsOutputtingImageFile => Config.Mode is ExportMode.ImageAtlas or ExportMode.ImageSequence;

	[Feature( "General", Icon = "settings" )]
	[FileExtensions( Extensions = "mp4,webm" )]
	[ShowIf( nameof( IsOutputtingImageFile ), false )]
	public string OutputVideoFile
	{
		get => Session.Cookies.ExportPath;
		set => Session.Cookies.ExportPath = value;
	}

	[Feature( "General", Icon = "settings" )]
	[FileExtensions( Extensions = "png,jpg,jpeg" )]
	[ShowIf( nameof( IsOutputtingImageFile ), true )]
	public string OutputImageFile
	{
		get => Session.Cookies.ImageSequencePath;
		set => Session.Cookies.ImageSequencePath = value;
	}

	public string OutputFile => IsOutputtingImageFile ? OutputImageFile : OutputVideoFile;

	[Feature( "General", Icon = "settings" )]
	public bool PlayAfterExport { get; set; } = true;

	[Hide]
	public MovieTimeRange TimeRange => (StartTime, EndTime);

	[Feature( "General", Icon = "settings" )]
	public double StartTime
	{
		get => Session.LoopTimeRange?.Start.TotalSeconds ?? 0d;
		set
		{
			var startSeconds = Math.Clamp( value, 0d, Session.Duration.TotalSeconds );
			var endSeconds = Math.Clamp( Session.LoopTimeRange?.End.TotalSeconds ?? Session.Duration.TotalSeconds, startSeconds, Session.Duration.TotalSeconds );

			Session.LoopTimeRange = (startSeconds, endSeconds);
		}
	}

	[Feature( "General", Icon = "settings" )]
	public double EndTime
	{
		get => Session.LoopTimeRange?.End.TotalSeconds ?? Session.Duration.TotalSeconds;
		set
		{
			var endSeconds = Math.Clamp( value, 0d, Session.Duration.TotalSeconds );
			var startSeconds = Math.Clamp( Session.LoopTimeRange?.Start.TotalSeconds ?? 0d, 0d, endSeconds );

			Session.LoopTimeRange = (startSeconds, endSeconds);
		}
	}

	/// <summary>
	/// How many frames the exported video will have.
	/// </summary>
	[Property]
	public string TotalFrames => $"{TimeRange.Duration.GetFrameCount( Config.FrameRate ):N0}";

	/// <summary>
	/// How many frames will be rendered in total. Many sub-frames get merged into one frame in the exported video.
	/// </summary>
	[Property, Title( "Total Sub-Frames" )]
	public string TotalSubFrames => $"{TimeRange.Duration.GetFrameCount( Config.FrameRate ) * Config.SubFramesPerFrame:N0}";

	/// <summary>
	/// Roughly how big the exported video will be on disk.
	/// </summary>
	[Property]
	public string EstimatedSize => $"{TimeRange.Duration.TotalSeconds * Config.Bitrate / 8d:N1} MB";

	public bool IsExporting => _exportTask is { IsCompleted: false };

	public bool SupportsTransparency => Path.GetExtension( OutputFile ).ToLower() is ".png" or ".webm";

	private CancellationTokenSource? _previewCts;

	private VideoExportWindow( Session session )
	{
		Session = session;
		Config = session.Project.ExportConfig ??= new VideoExportConfig();

		SetModal( true, true );

		Size = new Vector2( 1024, 400 );
		MinimumSize = Size;
		TranslucentBackground = true;
		NoSystemBackground = true;

		WindowTitle = "Export Video";
		SetWindowIcon( "video_file" );

		Layout = Layout.Row();
		Layout.Margin = new Margin( 32f, 32f, 16f, 32f );
		Layout.Spacing = 16f;

		// Preview

		var leftPanel = new Panel { Layout = Layout.Column() };
		var leftColumn = leftPanel.Layout;

		_preview = leftColumn.Add( new VideoExportPreview( this ), 1 );
		_timeline = leftColumn.Add( new VideoExportTimeline( this ), 0 );

		var rightPanel = new Widget { Layout = Layout.Column(), FixedWidth = 400 };
		var rightColumn = rightPanel.Layout;

		Layout.Add( leftPanel, 1 );
		Layout.Add( rightPanel, 0 );

		rightColumn.Spacing = 8f;

		// Control sheet

		var exportSerialized = EditorTypeLibrary.GetSerializedObject( this );
		var configSerialized = EditorTypeLibrary.GetSerializedObject( Config );

		var controlSheet = new ControlSheet();
		var properties = exportSerialized
			.Concat( configSerialized )
			.Where( x => x.HasAttribute<FeatureAttribute>() )
			.ToList();

		IControlSheet.FilterSortAndAdd( controlSheet, properties );

		exportSerialized.OnPropertyChanged += OnExportChanged;
		configSerialized.OnPropertyChanged += OnConfigChanged;

		controlSheet.SetMinimumColumnWidth( 0, 200 );

		rightColumn.Add( controlSheet );
		rightColumn.AddStretchCell();

		var summarySheet = new ControlSheet();

		rightColumn.Add( summarySheet );

		var summaryProperties = exportSerialized
			.Where( x => x.HasAttribute<PropertyAttribute>() )
			.ToList();

		IControlSheet.FilterSortAndAdd( summarySheet, summaryProperties );

		// Buttons

		var buttonRow = rightColumn.AddRow();

		buttonRow.Margin = new Margin( 8f, 0f, 16f, 0f );
		buttonRow.Spacing = 8f;

		_exportButton = buttonRow.Add( new Button.Primary( "Export", "ondemand_video" ), 2 );
		_cancelButton = buttonRow.Add( new Button.Danger( "Cancel", "cancel_presentation" ), 2 );

		_cancelButton.Visible = false;

		_exportButton.Clicked += ExportClicked;
		_cancelButton.Clicked += CancelClicked;

		PreviewTime = TimeRange.Start;

		_ = UpdatePreviewAsync( false );
	}

	private void OnExportChanged( SerializedProperty property )
	{
		_exportButton.Enabled = !string.IsNullOrWhiteSpace( OutputFile );

		// If we change start / end time, show that time
		// in the preview to help with seeking

		if ( _oldTimeRange.Start != TimeRange.Start )
		{
			PreviewTime = TimeRange.Start;
		}
		else if ( _oldTimeRange.End != TimeRange.End )
		{
			PreviewTime = TimeRange.End;
		}
		else
		{
			_timeline.Update();
			return;
		}

		_oldTimeRange = TimeRange;

		_ = UpdatePreviewAsync( true, TimeSpan.FromSeconds( 0.5 ) );
	}

	private void OnConfigChanged( SerializedProperty property )
	{
		_ = UpdatePreviewAsync( false, TimeSpan.FromSeconds( 0.5 ) );

		_preview.Update();
		_timeline.Update();
	}

	public async Task UpdatePreviewAsync( bool fast, TimeSpan delay = default )
	{
		if ( _previewCts is { } cts )
		{
			await cts.CancelAsync();
		}

		cts = _previewCts = new CancellationTokenSource();

		var config = new VideoExportConfig
		{
			Resolution = Config.Resolution,
			FrameRate = Config.FrameRate,
			WarmupFrameCount = 0,
			Exposure = fast ? Exposure.Instant : Config.Exposure,
			MotionQuality = Config.MotionQuality,
		};

		if ( config.Resolution.x < 4 || config.Resolution.y < 4 || config.FrameRate <= 0 )
		{
			return;
		}

		try
		{
			_preview.IsUpdating = true;
			_timeline.Update();

			if ( delay > TimeSpan.Zero )
			{
				await Task.Delay( delay, cts.Token );
				await MainThread.Wait();
			}

			var resolution = config.Resolution;

			await Session.Renderer.RenderAsync( PreviewTime, config, ( subFrameTime, pixels, _ ) =>
			{
				OnRenderFrame( resolution, subFrameTime, pixels.AsSpan(), ref _renderPixmap );
				return Task.CompletedTask;
			}, cts.Token );

			if ( fast && config.Exposure != Config.Exposure )
			{
				// If we did a fast preview, do a slow one if no options change
				// within UpdateRenderDelaySeconds

				_slowPreviewTime = RealTime.Now + SlowPreviewDelaySeconds;
			}
		}
		finally
		{
			_preview.IsUpdating = false;
			_preview.Update();
		}
	}

	private async Task ExportAsync()
	{
		_exportButton.Visible = false;
		_cancelButton.Visible = true;

		if ( _previewCts is { } cts )
		{
			await cts.CancelAsync();
		}

		_cts = new CancellationTokenSource();

		try
		{
			PreviewTime = TimeRange.Start;

			switch ( Config.Mode )
			{
				case ExportMode.VideoFile:
					await ExportVideoFile( _cts.Token );
					break;

				case ExportMode.ImageSequence:
					await ExportImageSequence( _cts.Token );
					break;

				case ExportMode.ImageAtlas:
					await ExportImageAtlas( _cts.Token );
					break;
			}
		}
		finally
		{
			RenderStartTime = 0d;
			LastRenderedTime = 0d;

			_exportButton.Visible = true;
			_cancelButton.Visible = false;
		}
	}

	private async Task ExportVideoFile( CancellationToken ct )
	{
		var resolution = Config.Resolution;

		using var writer = EditorUtility.CreateVideoWriter( OutputFile, Config.GetVideoWriterConfig( OutputFile ) );

		await Session.Renderer.RenderAsync( TimeRange, Config, ( time, pixels, innerCt ) =>
		{
			OnExportFrame( time, resolution, pixels );

			return Task.Run( () => writer.AddFrame( pixels.AsSpan() ), innerCt );
		}, ct );

		await writer.FinishAsync();

		if ( PlayAfterExport )
		{
			EditorUtility.OpenFile( OutputFile );
		}
	}

	private enum ImageFileFormat
	{
		Png,
		Jpeg
	}

	private static ImageFileFormat GetImageFormat( string path )
	{
		return Path.GetExtension( path ).ToLower() switch
		{
			".jpg" or ".jpeg" => ImageFileFormat.Jpeg,
			_ => ImageFileFormat.Png
		};
	}

	private static void SavePixmap( Pixmap pixmap, string path )
	{
		var fileFormat = GetImageFormat( path );

		switch ( fileFormat )
		{
			case ImageFileFormat.Png:
				pixmap.SavePng( path );
				break;

			case ImageFileFormat.Jpeg:
				pixmap.SaveJpg( path );
				break;
		}
	}

	private async Task ExportImageSequence( CancellationToken ct )
	{
		var resolution = Config.Resolution;

		var imagePathFormat = GetFrameFilePathFormat( OutputFile );
		var pixmap = new Pixmap( resolution.x, resolution.y );

		await Session.Renderer.RenderAsync( TimeRange, Config, ( time, pixels, innerCt ) =>
		{
			OnExportFrame( time, resolution, pixels );

			return Task.Run( () =>
			{
				pixmap.UpdateFromPixels( pixels, pixmap.Width, pixmap.Height, ImageFormat.RGBA8888 );

				var frameIndex = time.GetFrameIndex( Config.FrameRate );
				var framePath = string.Format( imagePathFormat, frameIndex );

				SavePixmap( pixmap, framePath );
			}, innerCt );
		}, ct );
	}

	private async Task ExportImageAtlas( CancellationToken ct )
	{
		var resolution = Config.Resolution;
		var frameCount = TimeRange.Duration.GetFrameCount( Config.FrameRate );
		var cols = (int)Math.Ceiling( Math.Sqrt( frameCount ) );
		var rows = (int)Math.Ceiling( (double)frameCount / cols );

		var atlasResolution = resolution * new Vector2Int( cols, rows );

		const int warnSize = 10_000;

		if ( atlasResolution.x >= warnSize || atlasResolution.y >= warnSize )
		{
			var confirmTcs = new TaskCompletionSource<bool>();

			Dialog.AskConfirm( () => confirmTcs.SetResult( true ), () => confirmTcs.SetResult( false ),
				$"The atlas will be {atlasResolution.x:N0}x{atlasResolution.y:N0}! Are you sure?" );

			if ( !await confirmTcs.Task )
			{
				return;
			}
		}

		var atlasPixels = new byte[atlasResolution.x * atlasResolution.y * 4];

		await Session.Renderer.RenderAsync( TimeRange, Config, ( time, pixels, innerCt ) =>
		{
			OnExportFrame( time, resolution, pixels );

			return Task.Run( () =>
			{
				var frameIndex = time.GetFrameIndex( Config.FrameRate );
				var col = frameIndex % cols;
				var row = frameIndex / cols;

				for ( var y = 0; y < resolution.y; ++y )
				{
					var srcLine = pixels.AsSpan( y * resolution.x * 4, resolution.x * 4 );
					var dstLine = atlasPixels.AsSpan( (y + row * resolution.y) * atlasResolution.x * 4, atlasResolution.x * 4 );

					srcLine.CopyTo( dstLine[(col * resolution.x * 4)..((col + 1) * resolution.x * 4)] );
				}

			}, innerCt );
		}, ct );

		var pixmap = new Pixmap( atlasResolution.x, atlasResolution.y );

		pixmap.UpdateFromPixels( atlasPixels, atlasResolution, ImageFormat.RGBA8888 );

		SavePixmap( pixmap, OutputFile );
	}

	private static Regex ImageSequenceFilePathRegex { get; } =
		new( @"^(?<path>.+[^0-9])(?:[0-9]+)?\.(?<ext>[a-z0-9]+)$" );

	private static string GetFrameFilePathFormat( string filePath )
	{
		if ( ImageSequenceFilePathRegex.Match( filePath ) is { Success: true } match )
		{
			return $"{match.Groups["path"].Value}{{0}}.{match.Groups["ext"]}";
		}

		return $"{filePath}{{0}}.png";
	}

	private void OnExportFrame( MovieTime time, Vector2Int resolution, byte[] pixels )
	{
		PreviewTime = time;
		LastRenderedTime = time;

		OnRenderFrame( resolution, time, pixels.AsSpan(), ref _previewPixmap );
	}

	private void ExportClicked()
	{
		if ( IsExporting ) return;
		if ( string.IsNullOrWhiteSpace( OutputFile ) ) return;

		RenderStartTime = TimeRange.Start;
		LastRenderedTime = TimeRange.Start;

		_exportTask = ExportAsync();
	}

	private void CancelClicked()
	{
		_cts?.Cancel();
	}

	[EditorEvent.Frame]
	private void Frame()
	{
		// Do a full preview render if we've not edited any options in a while

		if ( Application.MouseButtons != MouseButtons.None ) return;
		if ( _slowPreviewTime <= 0f ) return;
		if ( RealTime.Now < _slowPreviewTime ) return;

		_slowPreviewTime = 0f;
		_ = UpdatePreviewAsync( false );
	}

	private void OnRenderFrame( Vector2Int resolution, MovieTime time, ReadOnlySpan<byte> pixels, ref Pixmap? pixmap )
	{
		if ( pixmap?.Size != resolution )
		{
			pixmap = new Pixmap( Config.Resolution );
		}

		lock ( pixmap )
		{
			pixmap.UpdateFromPixels( pixels, Config.Resolution, ImageFormat.RGBA8888 );
		}

		LastRenderedFrame = pixmap;

		_preview.Update();
		_timeline.Update();
	}
}

file sealed class Panel : Widget
{
	public Color Color { get; set; } = Theme.ControlBackground;

	public Panel()
	{
		HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( Color );
		Paint.DrawRect( LocalRect, 4f );
	}
}

/// <summary>
/// Shows a preview render of one frame.
/// </summary>
internal sealed class VideoExportPreview : Widget
{
	public new VideoExportWindow Parent { get; }

	public bool IsUpdating { get; set; }

	private readonly Pixmap _gridPixmap;

	public VideoExportPreview( VideoExportWindow parent )
		: base( parent )
	{
		Parent = parent;

		MinimumWidth = 384;
		MinimumHeight = 256;

		HorizontalSizeMode = SizeMode.Expand | SizeMode.CanGrow;
		VerticalSizeMode = SizeMode.Default;

		_gridPixmap = new Pixmap( 16, 16 );

		using var paint = Paint.ToPixmap( _gridPixmap );

		Paint.ClearPen();
		Paint.SetBrush( Color.Black.LerpTo( Color.White, 0.5f ) );
		Paint.DrawRect( new Rect( 0f, 0f, 8f, 8f ) );
		Paint.DrawRect( new Rect( 8f, 8f, 8f, 8f ) );
		Paint.SetBrush( Color.Black.LerpTo( Color.White, 0.625f ) );
		Paint.DrawRect( new Rect( 0f, 8f, 8f, 8f ) );
		Paint.DrawRect( new Rect( 8f, 0f, 8f, 8f ) );
	}

	protected override void OnPaint()
	{
		var outerRect = LocalRect.Shrink( 8f, 8f, 8f, 0f );
		var innerRect = outerRect.Contain( Parent.Config.Resolution, stretch: true );

		Paint.ClearPen();

		if ( Parent.SupportsTransparency )
		{
			Paint.SetBrush( _gridPixmap );
		}
		else
		{
			Paint.SetBrush( Color.Black );
		}

		Paint.DrawRect( outerRect );

		if ( Parent.LastRenderedFrame is { } frame )
		{
			Paint.SetBrush( Color.White );

			lock ( frame )
			{
				Paint.Draw( innerRect, frame );
			}
		}
		else
		{
			Paint.SetBrushAndPen( Theme.SelectedBackground );
			Paint.DrawRect( innerRect );
		}

		if ( IsUpdating )
		{
			var updateSpinnerRect = innerRect.Shrink( 8f ).Contain( 128f );

			Paint.SetBrushAndPen( Color.Black.WithAlpha( 0.75f ) );
			Paint.DrawRect( updateSpinnerRect, 4f );
			Paint.SetPen( Theme.TextControl );
			Paint.DrawIcon( updateSpinnerRect, "autorenew", 128f );
		}
	}
}

/// <summary>
/// Progress bar / timeline under the preview in the video export window.
/// </summary>
internal sealed class VideoExportTimeline : Widget
{
	public new VideoExportWindow Parent { get; }

	public VideoExportTimeline( VideoExportWindow parent )
		: base( parent )
	{
		Parent = parent;

		HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
		FixedHeight = 24f;
		MouseTracking = true;
	}

	private enum Element
	{
		Start,
		End,
		Playhead
	}

	private Element? _draggedElement;

	private Rect InnerRect => LocalRect.Shrink( 8f );

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( GetDragElement( e.LocalPosition ) is { } element )
		{
			_draggedElement = element;
		}
		else
		{
			_draggedElement = Element.Playhead;
		}

		Cursor = CursorShape.SizeH;
	}

	private void SetElementTime( Element element, MovieTime time )
	{
		switch ( element )
		{
			case Element.Start:
				Parent.StartTime = time.TotalSeconds;
				break;

			case Element.End:
				Parent.EndTime = time.TotalSeconds;
				break;
		}

		Parent.PreviewTime = time;
	}

	private MovieTime GetElementTime( Element element ) => element switch
	{
		Element.Start => Parent.TimeRange.Start,
		Element.End => Parent.TimeRange.End,
		Element.Playhead => Parent.PreviewTime,
		_ => throw new NotImplementedException()
	};

	private Element? GetDragElement( Vector2 localPos )
	{
		const float hoverMargin = 4f;

		Element? bestElement = null;
		var bestDist = hoverMargin;

		foreach ( var element in Enum.GetValues<Element>() )
		{
			var time = GetElementTime( element );
			var elementX = TimeToLocalX( time );

			var dist = Math.Abs( elementX - localPos.x );

			if ( dist < bestDist )
			{
				bestDist = dist;
				bestElement = element;
			}
		}

		return bestElement;
	}

	private float TimeToLocalX( MovieTime time )
	{
		var timelineRect = InnerRect;
		var fraction = Parent.Session.Duration.GetFraction( time );
		return timelineRect.Left + fraction * timelineRect.Width;
	}

	private Rect TimeRangeToLocalRect( MovieTimeRange timeRange )
	{
		var timelineRect = InnerRect;
		var leftFrac = Parent.Session.Duration.GetFraction( timeRange.Start );
		var rightFrac = Parent.Session.Duration.GetFraction( timeRange.End );

		return new Rect( timelineRect.Left + leftFrac * timelineRect.Width,
			timelineRect.Top,
			(rightFrac - leftFrac) * timelineRect.Width,
			timelineRect.Height );
	}

	private MovieTime LocalXToTime( float localX )
	{
		var timelineRect = InnerRect;
		var relX = (localX - timelineRect.Left) / timelineRect.Width;

		return MovieTime.Lerp( 0d, Parent.Session.Duration, relX );
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );

		if ( _draggedElement is { } element )
		{
			var snap = (Application.KeyboardModifiers & KeyboardModifiers.Ctrl) != 0
				? MovieTime.FromSeconds( 0.1 )
				: MovieTime.FromFrames( 1, Parent.Config.FrameRate );

			SetElementTime( element, LocalXToTime( e.LocalPosition.x ).Round( snap ) );
			_ = Parent.UpdatePreviewAsync( true );
		}
		else
		{
			UpdateCursor( e );
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		_draggedElement = null;

		UpdateCursor( e );
	}

	private void UpdateCursor( MouseEvent e )
	{
		if ( GetDragElement( e.LocalPosition ) is not null )
		{
			Cursor = CursorShape.SizeH;
		}
		else
		{
			Cursor = CursorShape.Arrow;
		}
	}

	protected override void OnPaint()
	{
		var timelineRect = InnerRect;

		Paint.Antialiasing = true;

		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground.Darken( 0.5f ) );
		Paint.DrawRect( timelineRect, 2f );

		Paint.SetBrush( Theme.SurfaceBackground );
		Paint.DrawRect( TimeRangeToLocalRect( Parent.TimeRange ), 2f );

		Paint.SetBrush( Theme.Primary );
		Paint.DrawRect( TimeRangeToLocalRect( (Parent.RenderStartTime, Parent.LastRenderedTime) ), 2f );

		var previewX = TimeToLocalX( Parent.PreviewTime ).Floor();

		Paint.SetBrush( Theme.Yellow );
		PaintExtensions.PaintBookmarkDown( previewX, timelineRect.Top, 3f, 3f, 6f );
		PaintExtensions.PaintBookmarkUp( previewX, timelineRect.Bottom, 3f, 3f, 6f );
		Paint.SetPen( Theme.Yellow );
		Paint.DrawLine( new Vector2( previewX, timelineRect.Top - 1f ), new Vector2( previewX, timelineRect.Bottom + 1f ) );
	}
}
