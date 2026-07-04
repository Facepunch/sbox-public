namespace Editor.SoundEditor;

public class Timeline : Widget
{
	private readonly TimelineView TimelineView;

	public bool Playing { get; set; }
	public bool Repeating { get; set; }
	public float Time { get; private set; }
	public List<VisemeFrame> Frames;

	private readonly Option PlayOption;
	private readonly Option PlayFromStartOption;
	private readonly Option RepeatOption;

	public Timeline( Widget parent ) : base( parent )
	{
		Name = "Timeline";
		WindowTitle = "Timeline";
		SetWindowIcon( "timeline" );

		Layout = Layout.Column();

		var toolbar = new ToolBar( this );
		toolbar.SetIconSize( 18 );
		PlayOption = toolbar.AddOption( "Play", "play_arrow", () => Playing = !Playing );
		PlayFromStartOption = toolbar.AddOption( "Play From Start", "skip_previous", () => PlayFromStart() );
		RepeatOption = toolbar.AddOption( "Repeat Off", "repeat", () => Repeating = !Repeating );
		RepeatOption.Checkable = true;

		TimelineView = new TimelineView( this );

		Layout.Add( toolbar );
		Layout.Add( TimelineView, 1 );
	}

	public void PlayFromStart()
	{
		if ( Playing )
			return;

		TimelineView.Time = 0;
		Playing = true;
	}

	/// <summary>
	/// Replace the timeline's visemes.
	/// </summary>
	public void SetVisemes( List<VisemeFrame> frames )
	{
		Frames = frames;
		TimelineView.SetVisemes( frames );
	}

	public void SetSamples( short[] samples, float duration, string sound )
	{
		TimelineView.SetSamples( samples, duration, sound );
	}

	public void SetAsset( Asset asset )
	{
		if ( asset == null )
			return;

		if ( asset.MetaData == null )
			return;

		Frames = asset.MetaData.Get<List<VisemeFrame>>( "visemes" );
		TimelineView.SetVisemes( Frames );
	}

	[EditorEvent.Frame]
	protected void OnFrame()
	{
		TimelineView.OnFrame();
		Time = TimelineView.Time;

		PlayOption.Text = Playing ? "Pause" : "Play";
		PlayOption.Icon = Playing ? "pause" : "play_arrow";
		RepeatOption.Text = Repeating ? "Repeat Off" : "Repeat On";
		RepeatOption.Checked = Repeating;
	}

	[Shortcut( "sound.play", "SPACE", ShortcutType.Window )]
	private void TogglePlayback()
	{
		Playing = !Playing;
	}

	[Shortcut( "sound.play-from-start", "CTRL+SPACE", ShortcutType.Window )]
	private void PlayFromStartShortcut()
	{
		PlayFromStart();
	}
}

public class TimelineView : GraphicsView
{
	private readonly Timeline Timeline;
	private readonly TimeAxis TimeAxis;
	private readonly Scrubber Scrubber;
	private readonly WaveForm WaveForm;
	private readonly List<VisemeItem> VisemeItems = new();

	public float Duration { get; private set; }
	public float ZoomFactor { get; private set; }
	public float Time { get; set; }
	public bool Scrubbing { get; set; }
	public string Sound { get; private set; }
	public SoundHandle SoundHandle { get; private set; }

	public TimelineView( Timeline parent ) : base( parent )
	{
		Timeline = parent;
		SceneRect = new( 0, Size );
		HorizontalScrollbar = ScrollbarMode.On;
		VerticalScrollbar = ScrollbarMode.Off;
		Scale = 1;
		ZoomFactor = 1;
		Time = 0;

		WaveForm = new WaveForm( this );
		Add( WaveForm );

		TimeAxis = new TimeAxis( this );
		Add( TimeAxis );

		Scrubber = new Scrubber( this );
		Add( Scrubber );

		DoLayout();
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		var size = Size;
		size.x = MathF.Max( Size.x, PositionFromTime( Duration ) );
		SceneRect = new( 0, size );
		TimeAxis.Size = new Vector2( size.x, Theme.RowHeight );
		Scrubber.Size = new Vector2( 9, size.y );

		var r = SceneRect;
		r.Top = TimeAxis.SceneRect.Bottom;
		WaveForm.SceneRect = r;

		Scrubber.Position = Scrubber.Position.WithX( PositionFromTime( Time ) - 3 ).SnapToGrid( 1.0f );

		foreach ( var item in VisemeItems )
		{
			item.Position = new Vector2( PositionFromTime( item.Frame.StartTime ), Theme.RowHeight );
			item.Size = new Vector2( PositionFromTime( item.Frame.EndTime - item.Frame.StartTime ), SceneRect.Bottom - Theme.RowHeight );
		}
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		SoundHandle?.Stop( 0.0f );
		SoundHandle = null;
	}

	public void OnFrame()
	{
		Time = Time.Clamp( 0, Duration );

		if ( !Timeline.Playing )
		{
			SoundHandle?.Stop( 0.0f );
			SoundHandle = null;
		}

		if ( Scrubbing )
		{
			Timeline.Playing = false;
			SoundHandle?.Stop( 0.0f );
			SoundHandle = EditorUtility.PlaySound( Sound, Time );
		}

		if ( Timeline.Playing )
		{
			Time += RealTime.Delta;
			var time = Time % Duration;
			if ( time < Time )
			{
				if ( Timeline.Repeating )
				{
					Time = time;
					SoundHandle?.Stop( 0.0f );
					SoundHandle = EditorUtility.PlaySound( Sound, Time );
				}
				else
				{
					time = 0;
					Time = time;
					SoundHandle?.Stop( 0.0f );
					Timeline.Playing = false;
				}
			}

			if ( Timeline.Playing && !SoundHandle.IsValid() )
			{
				SoundHandle = EditorUtility.PlaySound( Sound, Time );
			}

			Scrubber.Position = Scrubber.Position.WithX( PositionFromTime( Time ) - 3 ).SnapToGrid( 1.0f );
			CenterOn( new Vector2( Scrubber.Position.x, 0 ) );
			TimeAxis.Update();
			WaveForm.Update();
		}

		Scrubbing = false;
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		base.OnMouseWheel( e );

		e.Accept();

		ZoomFactor += e.Delta * 0.001f;
		ZoomFactor = ZoomFactor.Clamp( 0.5f, 10 );

		DoLayout();

		CenterOn( Scrubber.Position );

		WaveForm.CreateWaveLines();
		TimeAxis.Update();
	}

	public float PositionFromTime( float time )
	{
		return 1000 * ZoomFactor * time;
	}

	public float TimeFromPosition( float position )
	{
		return position / (1000 * ZoomFactor);
	}

	public void SetSamples( short[] samples, float duration, string sound )
	{
		Sound = sound;
		Duration = duration;
		WaveForm.SetSamples( samples, duration );
	}

	public void SetVisemes( List<VisemeFrame> frames )
	{
		ClearVisemes();

		if ( frames == null )
			return;

		foreach ( var frame in frames )
		{
			AddItem( frame );
		}
	}

	private void AddItem( VisemeFrame frame )
	{
		var item = new VisemeItem( this, frame );
		item.Position = new Vector2( PositionFromTime( frame.StartTime ), Theme.RowHeight );
		item.Size = new Vector2( PositionFromTime( frame.EndTime - frame.StartTime ), SceneRect.Bottom - Theme.RowHeight );
		VisemeItems.Add( item );
		Add( item );
	}

	public void ClearVisemes()
	{
		foreach ( var item in VisemeItems.ToArray() )
			item.Destroy();

		VisemeItems.Clear();
	}

	public void MoveScrubber( float position )
	{
		Scrubber.Position = Vector2.Right * (position - 4).SnapToGrid( 1.0f );
		Time = TimeFromPosition( Scrubber.Position.x + 4 );
		Timeline.Playing = false;
	}

	internal void VisemeKeyPress( KeyEvent e )
	{
		var items = VisemeItems.Where( x => x.Selected ).ToArray();

		if ( e.Key == KeyCode.Delete )
		{
			foreach ( var item in items )
			{
				Delete( item );
			}
		}
	}

	internal void Delete( VisemeItem item )
	{
		if ( VisemeItems.Remove( item ) )
		{
			item.Destroy();
		}
	}

	internal void UpdateFrames()
	{
		Timeline.Frames = VisemeItems.Select( x => x.Frame ).ToList();
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		base.OnContextMenu( e );

		var time = TimeFromPosition( ToScene( e.LocalPosition ).x );
		var menu = new ContextMenu( this );

		var visemeMenu = menu.AddMenu( "Create Viseme" );

		// Skip silence (0); there's nothing to place for it.
		for ( int viseme = 1; viseme < LipSyncGenerator.Count; viseme++ )
		{
			var v = viseme;
			visemeMenu.AddOption( LipSyncGenerator.Label( v ), null, () => CreateViseme( v, time ) );
		}

		menu.OpenAt( e.ScreenPosition );
	}

	private void CreateViseme( int viseme, float time )
	{
		AddItem( new VisemeFrame { Viseme = viseme, StartTime = time, EndTime = time + 0.1f } );
		UpdateFrames();
	}
}

public class WaveForm : GraphicsItem
{
	private struct WaveLine
	{
		public float top;
		public float bottom;
	}

	private readonly TimelineView TimelineView;
	private short[] Samples;
	private float Duration;
	private readonly List<WaveLine> WaveLines = new();
	private short MinSample = short.MaxValue;
	private short MaxSample = short.MinValue;

	public WaveForm( TimelineView view )
	{
		TimelineView = view;
		ZIndex = -1;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.Antialiasing = false;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect );

		if ( WaveLines.Count > 0 )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Primary );
			var top = 0;
			var height = LocalRect.Height;

			for ( int i = 0; i < WaveLines.Count; ++i )
			{
				var line = WaveLines[i];
				float lo = top + (height * line.top);
				float hi = top + (height * line.bottom);
				var size = MathF.Ceiling( MathF.Max( 1, lo - hi ) );
				var y = height * 0.5f - (size * 0.5f);
				y = MathF.Round( MathF.Max( 1, y ) );
				var r = new Rect( new Vector2( i * 4, y ), new Vector2( 3, size ) );

				Paint.DrawRect( r );
			}
		}
	}

	public void SetSamples( short[] samples, float duration )
	{
		Samples = samples;
		Duration = duration;
		CreateWaveLines();
	}

	public void CreateWaveLines()
	{
		MinSample = short.MaxValue;
		MaxSample = short.MinValue;

		WaveLines.Clear();

		if ( Samples == null || Samples.Length == 0 )
			return;

		var sampleCount = Samples.Length;

		for ( int i = 0; i < sampleCount; i++ )
		{
			var sample = Samples[i];
			MinSample = Math.Min( sample, MinSample );
			MaxSample = Math.Max( sample, MaxSample );
		}

		var waveformWidth = TimelineView.PositionFromTime( Duration ) / 4.0f;
		var duration = Duration;
		if ( duration <= 0 ) return;

		var timePerSample = duration / (sampleCount - 1);
		var timePerPixel = duration / (waveformWidth - 1);
		var pixelTime = 0.0f;

		int minVal = Math.Max( Math.Abs( (int)MinSample ), Math.Abs( (int)MaxSample ) );
		int maxVal = -minVal;

		float fRange = maxVal - minVal;

		for ( int pi = 0; pi < waveformWidth; ++pi, pixelTime += timePerPixel )
		{
			short lo = short.MaxValue;
			short hi = short.MinValue;

			int s0 = (int)(pixelTime / timePerSample);
			int s1 = Math.Max( (int)((pixelTime + timePerPixel) / timePerSample), s0 + 1 );
			int sn = Math.Min( sampleCount, s1 );

			if ( s0 >= sn )
				continue;

			for ( int si = s0; si < sn; ++si )
			{
				var sample = Samples[si];
				lo = Math.Min( sample, lo );
				hi = Math.Max( sample, hi );
			}

			WaveLines.Add( new WaveLine
			{
				top = fRange != 0.0f ? (lo - minVal) / fRange : 0.5f,
				bottom = fRange != 0.0f ? (hi - minVal) / fRange : 0.5f
			} );
		}

		Update();
	}
}

public class TimeAxis : GraphicsItem
{
	private readonly TimelineView TimelineView;

	public TimeAxis( TimelineView view )
	{
		TimelineView = view;
		ZIndex = -1;
		HoverEvents = true;
	}

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		base.OnMousePressed( e );

		if ( e.LeftMouseButton )
		{
			TimelineView.MoveScrubber( e.LocalPosition.x );
		}
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.Antialiasing = false;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect );

		Paint.SetDefaultFont( 7 );

		var rect = LocalRect.Shrink( 1 );
		var zoomFactor = TimelineView.ZoomFactor;

		var spacing = 100 * zoomFactor;
		var lines = rect.Width / spacing;
		var w = spacing;
		var subdivisions = (int)(10 * zoomFactor);
		var subLineSpacing = w / subdivisions;

		for ( int i = 0; i < lines; ++i )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawLine( new Vector2( rect.Left + w * i, rect.Bottom ), new Vector2( rect.Left + w * i, rect.Bottom - 8 ) );
			Paint.DrawText( new Vector2( rect.Left + w * i, rect.Top ), $"{100 * i}" );
			Paint.SetPen( Theme.Text.WithAlpha( 0.2f ) );

			for ( int j = 1; j < subdivisions; ++j )
			{
				var subLineX = w * i + subLineSpacing * j;
				Paint.DrawLine( new Vector2( rect.Left + subLineX, rect.Bottom ), new Vector2( rect.Left + subLineX, rect.Bottom - 4 ) );
			}
		}
	}
}

public class Scrubber : GraphicsItem
{
	private readonly TimelineView TimelineView;

	public Scrubber( TimelineView view )
	{
		TimelineView = view;
		ZIndex = -1;
		HoverEvents = true;
		Cursor = CursorShape.SizeH;
		Movable = true;
		Selectable = true;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.Antialiasing = false;
		Paint.ClearPen();
		Paint.SetBrush( Theme.Green.WithAlpha( 0.7f ) );
		Paint.DrawRect( new Rect( 0, new Vector2( LocalRect.Width, Theme.RowHeight + 1 ) ) );
		Paint.SetPen( Theme.Green.WithAlpha( 0.7f ) );
		Paint.DrawLine( new Vector2( 4, Theme.RowHeight + 1 ), new Vector2( 4, LocalRect.Bottom ) );
	}

	protected override void OnMoved()
	{
		base.OnMoved();

		TimelineView.Time = TimelineView.TimeFromPosition( Position.x );
		TimelineView.Scrubbing = true;

		Position = Position.WithY( 0 );
		Position = Position.WithX( MathF.Max( -4, Position.x ) );
	}
}

public class VisemeItem : GraphicsItem
{
	private readonly TimelineView TimelineView;
	public VisemeFrame Frame { get; private set; }

	[Flags]
	private enum SizeDirection
	{
		None = 0,
		Left = 1 << 2,
		Right = 1 << 3
	}

	private bool Resizing;
	private Vector2 Offset;
	private SizeDirection Direction;

	public VisemeItem( TimelineView view, VisemeFrame frame )
	{
		Frame = frame;

		TimelineView = view;
		ToolTip = LipSyncGenerator.Label( frame.Viseme );

		ZIndex = -1;
		HoverEvents = true;
		Selectable = true;
		Movable = true;
		Focusable = true;
	}

	protected override void OnMoved()
	{
		base.OnMoved();

		Position = Position.WithY( Theme.RowHeight );
		Position = Position.WithX( Position.x.Clamp( 0, TimelineView.PositionFromTime( TimelineView.Duration ) ) );

		UpdateFrame();
	}

	private void UpdateFrame()
	{
		var frame = Frame;
		frame.StartTime = TimelineView.TimeFromPosition( SceneRect.Left );
		frame.EndTime = TimelineView.TimeFromPosition( SceneRect.Right );
		Frame = frame;

		TimelineView.UpdateFrames();
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );

		TimelineView.VisemeKeyPress( e );
		TimelineView.UpdateFrames();
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.Antialiasing = false;
		Paint.ClearPen();
		if ( Paint.HasSelected )
			Paint.SetPen( Theme.Primary );
		Paint.SetBrush( Theme.Primary.WithAlpha( Paint.HasMouseOver || Paint.HasSelected ? 0.5f : 0.2f ) );
		Paint.DrawRect( LocalRect.Shrink( 1 ) );
		Paint.SetPen( Theme.Text );
		var r = LocalRect;
		r.Height = Theme.RowHeight;
		Paint.DrawText( r.Shrink( 2 ), LipSyncGenerator.Label( Frame.Viseme ) );
	}

	private Rect ResizeLeft( Rect rect, float position )
	{
		rect.Left = position;
		var size = rect.Right - rect.Left;

		Log.Info( size );
		size -= MathF.Max( 8, size );
		rect.Left += size;
		return rect;
	}

	private Rect ResizeRight( Rect rect, float position )
	{
		rect.Right = position;
		var size = rect.Right - rect.Left;
		size -= MathF.Max( 8, size );
		rect.Right -= size;
		return rect;
	}

	private void UpdateDirection( Vector2 position )
	{
		Direction = SizeDirection.None;

		Cursor = CursorShape.None;

		if ( !Selected )
			return;

		if ( position.x <= 4 )
		{
			Direction |= SizeDirection.Left;
			Offset.x = position.x;
		}
		else if ( position.x >= Size.x - 4 )
		{
			Direction |= SizeDirection.Right;
			Offset.x = position.x - Size.x;
		}

		if ( Direction.HasFlag( SizeDirection.Left ) || Direction.HasFlag( SizeDirection.Right ) )
		{
			Cursor = CursorShape.SizeH;
		}
		else
		{
			Cursor = CursorShape.None;
		}
	}

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		if ( Selected && e.LeftMouseButton && !Resizing )
		{
			UpdateDirection( e.LocalPosition );

			if ( Direction != SizeDirection.None )
			{
				Resizing = true;
				e.Accepted = true;
			}
		}

		if ( Resizing )
		{
			e.Accepted = true;
		}

		base.OnMousePressed( e );
	}

	protected override void OnMouseReleased( GraphicsMouseEvent e )
	{
		if ( Resizing )
		{
			e.Accepted = true;

			Resizing = false;
		}

		base.OnMouseReleased( e );
	}

	protected override void OnHoverMove( GraphicsHoverEvent e )
	{
		base.OnHoverMove( e );

		UpdateDirection( e.LocalPosition );
	}

	protected override void OnHoverEnter( GraphicsHoverEvent e )
	{
		base.OnHoverEnter( e );

		UpdateDirection( e.LocalPosition );
	}

	protected override void OnHoverLeave( GraphicsHoverEvent e )
	{
		base.OnHoverLeave( e );

		Direction = SizeDirection.None;
		Cursor = CursorShape.None;
	}

	protected override void OnMouseMove( GraphicsMouseEvent e )
	{
		base.OnMouseMove( e );

		if ( !Resizing )
			return;

		e.Accepted = true;

		var position = e.ScenePosition - Offset;
		var rect = SceneRect;

		if ( Direction.HasFlag( SizeDirection.Left ) )
			rect = ResizeLeft( rect, position.x );
		else if ( Direction.HasFlag( SizeDirection.Right ) )
			rect = ResizeRight( rect, position.x );

		SceneRect = rect;

		PrepareGeometryChange();
		Update();

		UpdateFrame();
	}
}
