using System.Text.Json.Serialization;

namespace Editor.SoundEditor;

public struct PhonemeFrame
{
	[JsonPropertyName( "phoneme" )]
	public int Code { get; set; }

	[JsonPropertyName( "start" )]
	public float StartTime { get; set; }

	[JsonPropertyName( "end" )]
	public float EndTime { get; set; }
}

public class Timeline : Widget
{
	private readonly TimelineView TimelineView;

	public bool Playing { get; set; }
	public bool Repeating { get; set; }
	public float Time { get; private set; }
	public List<PhonemeFrame> Frames;

	private readonly Option PlayOption;
	private readonly Option PlayFromStartOption;
	private readonly Option RepeatOption;

	private bool _prevPlay = false;

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

		Frames = asset.MetaData.Get<List<PhonemeFrame>>( "phonemes" );
		TimelineView.SetPhonemes( Frames );
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

		if ( Application.IsKeyDown( KeyCode.Space ) && Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) && !_prevPlay )
		{
			PlayFromStart();
		}
		else if ( Application.IsKeyDown( KeyCode.Space ) && !_prevPlay )
		{
			Playing = !Playing;
		}
		_prevPlay = Application.IsKeyDown( KeyCode.Space );
	}
}

public class TimelineView : GraphicsView
{
	private readonly Timeline Timeline;
	private readonly TimeAxis TimeAxis;
	private readonly Scrubber Scrubber;
	private readonly WaveForm WaveForm;
	private readonly List<PhonemeItem> PhonemeItems = new();

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

		foreach ( var item in PhonemeItems )
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

	public void SetPhonemes( List<PhonemeFrame> frames )
	{
		if ( frames == null )
			return;

		foreach ( var frame in frames )
		{
			var item = new PhonemeItem( this, frame );
			item.Position = new Vector2( PositionFromTime( frame.StartTime ), Theme.RowHeight );
			item.Size = new Vector2( PositionFromTime( frame.EndTime - frame.StartTime ), SceneRect.Bottom - Theme.RowHeight );
			PhonemeItems.Add( item );
			Add( item );
		}
	}

	public void MoveScrubber( float position )
	{
		Scrubber.Position = Vector2.Right * (position - 4).SnapToGrid( 1.0f );
		Time = TimeFromPosition( Scrubber.Position.x + 4 );
		Timeline.Playing = false;
	}

	internal void PhonemeKeyPress( KeyEvent e )
	{
		var items = PhonemeItems.Where( x => x.Selected ).ToArray();

		if ( e.Key == KeyCode.Delete )
		{
			foreach ( var item in items )
			{
				Delete( item );
			}
		}
	}

	internal void Delete( PhonemeItem item )
	{
		if ( PhonemeItems.Remove( item ) )
		{
			item.Destroy();
		}
	}

	internal void UpdateFrames()
	{
		Timeline.Frames = PhonemeItems.Select( x => x.Frame ).ToList();
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		base.OnContextMenu( e );

		var time = TimeFromPosition( ToScene( e.LocalPosition ).x );
		var menu = new ContextMenu( this );

		var groupedPhonemes = PhonemeItem.PhonemeDescriptions
							   .GroupBy( p => p.Value.Category )
							   .OrderBy( g => g.Key );

		var phonemeMenu = menu.AddMenu( $"Create Phoneme" );

		foreach ( var group in groupedPhonemes )
		{
			var submenu = phonemeMenu.AddMenu( group.Key.ToString().Replace( '_', ' ' ) );

			foreach ( var p in group )
			{
				submenu.AddOption( $"{p.Value.Name.ToUpper()} - {p.Value.Desc}", null, () => CreatePhoneme( p.Key, time ) );
			}
		}

		menu.OpenAt( e.ScreenPosition );
	}

	private void CreatePhoneme( int code, float time )
	{
		var frame = new PhonemeFrame { Code = code, StartTime = time, EndTime = time + 0.1f };
		var item = new PhonemeItem( this, frame );
		item.Position = new Vector2( PositionFromTime( time ), Theme.RowHeight );
		item.Size = new Vector2( PositionFromTime( frame.EndTime - frame.StartTime ), SceneRect.Bottom - Theme.RowHeight );
		PhonemeItems.Add( item );
		Add( item );

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

public class PhonemeItem : GraphicsItem
{
	private readonly TimelineView TimelineView;
	private readonly PhonemeDesc Desc;
	public PhonemeFrame Frame { get; private set; }

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

	public PhonemeItem( TimelineView view, PhonemeFrame frame )
	{
		Frame = frame;
		Desc = PhonemeDescriptions[frame.Code];

		TimelineView = view;
		ToolTip = $"{Desc.Name.ToUpper()} - {Desc.Desc}";

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

		TimelineView.PhonemeKeyPress( e );
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
		Paint.DrawText( r.Shrink( 2 ), Desc.Name.ToUpper() );
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

	public struct PhonemeDesc
	{
		public string Name { get; set; }
		public string Desc { get; set; }
		public PhonemeCategory Category { get; set; }
	}

	public enum PhonemeCategory
	{
		Stop_Plosive,
		Fricative,
		Affricate,
		Nasal,
		Approximant,
		Trill,
		Tap_Flap,
		Vowel,
		Rhotic_Vowel,
	}

	internal static readonly Dictionary<int, PhonemeDesc> PhonemeDescriptions = new()
	{
		{ 'b', new PhonemeDesc { Name = "b", Desc = "Big : voiced alveolar stop", Category = PhonemeCategory.Stop_Plosive } },
		{ 'm', new PhonemeDesc { Name = "m", Desc = "Mat : voiced bilabial nasal", Category = PhonemeCategory.Nasal } },
		{ 'p', new PhonemeDesc { Name = "p", Desc = "Put; voiceless alveolar stop", Category = PhonemeCategory.Stop_Plosive } },
		{ 'w', new PhonemeDesc { Name = "w", Desc = "With : voiced labial-velar approximant", Category = PhonemeCategory.Approximant } },
		{ 'f', new PhonemeDesc { Name = "f", Desc = "Fork : voiceless labiodental fricative", Category = PhonemeCategory.Fricative } },
		{ 'v', new PhonemeDesc { Name = "v", Desc = "Val : voiced labialdental fricative", Category = PhonemeCategory.Fricative } },
		{ 0x0279, new PhonemeDesc { Name = "r", Desc = "Red : voiced alveolar approximant", Category = PhonemeCategory.Approximant } },
		{ 'r', new PhonemeDesc { Name = "r2", Desc = "Red : voiced alveolar trill", Category = PhonemeCategory.Trill } },
		{ 0x027b, new PhonemeDesc { Name = "r3", Desc = "Red : voiced retroflex approximant", Category = PhonemeCategory.Approximant } },
		{ 0x025a, new PhonemeDesc { Name = "er", Desc = "URn : rhotacized schwa", Category = PhonemeCategory.Vowel } },
		{ 0x025d, new PhonemeDesc { Name = "er2", Desc = "URn : rhotacized lower-mid central vowel", Category = PhonemeCategory.Vowel } },
		{ 0x00f0, new PhonemeDesc { Name = "dh", Desc = "THen : voiced dental fricative", Category = PhonemeCategory.Fricative } },
		{ 0x03b8, new PhonemeDesc { Name = "th", Desc = "THin : voiceless dental fricative", Category = PhonemeCategory.Fricative } },
		{ 0x0283, new PhonemeDesc { Name = "sh", Desc = "SHe : voiceless postalveolar fricative", Category = PhonemeCategory.Fricative } },
		{ 0x02a4, new PhonemeDesc { Name = "jh", Desc = "Joy : voiced postalveolar afficate", Category = PhonemeCategory.Affricate } },
		{ 0x02a7, new PhonemeDesc { Name = "ch", Desc = "CHin : voiceless postalveolar affricate", Category = PhonemeCategory.Affricate } },
		{ 's', new PhonemeDesc { Name = "s", Desc = "Sit : voiceless alveolar fricative", Category = PhonemeCategory.Fricative } },
		{ 'z', new PhonemeDesc { Name = "z", Desc = "Zap : voiced alveolar fricative", Category = PhonemeCategory.Fricative } },
		{ 'd', new PhonemeDesc { Name = "d", Desc = "Dig : voiced bilabial stop", Category = PhonemeCategory.Stop_Plosive } },
		{ 0x027e, new PhonemeDesc { Name = "d2", Desc = "Dig : voiced alveolar flap or tap", Category = PhonemeCategory.Tap_Flap } },
		{ 'l', new PhonemeDesc { Name = "l", Desc = "Lid : voiced alveolar lateral approximant", Category = PhonemeCategory.Approximant } },
		{ 0x026b, new PhonemeDesc { Name = "l2", Desc = "Lid : velarized voiced alveolar lateral approximant", Category = PhonemeCategory.Approximant } },
		{ 'n', new PhonemeDesc { Name = "n", Desc = "No : voiced alveolar nasal", Category = PhonemeCategory.Nasal } },
		{ 't', new PhonemeDesc { Name = "t", Desc = "Talk : voiceless bilabial stop", Category = PhonemeCategory.Stop_Plosive } },
		{ 'o', new PhonemeDesc { Name = "ow", Desc = "gO : upper-mid back rounded vowel", Category = PhonemeCategory.Vowel } },
		{ 'u', new PhonemeDesc { Name = "uw", Desc = "tOO : high back rounded vowel", Category = PhonemeCategory.Vowel } },
		{ 'e', new PhonemeDesc { Name = "ey", Desc = "Ate : upper-mid front unrounded vowel", Category = PhonemeCategory.Vowel } },
		{ 0x00e6, new PhonemeDesc { Name = "ae", Desc = "cAt : semi-low front unrounded vowel", Category = PhonemeCategory.Vowel } },
		{ 0x0251, new PhonemeDesc { Name = "aa", Desc = "fAther : low back unrounded vowel", Category = PhonemeCategory.Vowel } },
		{ 'a', new PhonemeDesc { Name = "aa2", Desc = "fAther : low front unrounded vowel", Category = PhonemeCategory.Vowel } },
		{ 'i',  new PhonemeDesc { Name ="iy", Desc = "fEEl : high front unrounded vowel", Category = PhonemeCategory.Vowel } },
		{ 'j', new PhonemeDesc { Name = "y", Desc = "Yacht : voiced palatal approximant", Category = PhonemeCategory.Approximant } },
		{ 0x028c, new PhonemeDesc { Name = "ah", Desc = "cUt : lower-mid back unrounded vowel", Category = PhonemeCategory.Vowel } },
		{ 0x0254, new PhonemeDesc { Name = "ao",  Desc = "dOg : lower-mid back rounded vowel", Category = PhonemeCategory.Vowel } },
		{ 0x0259, new PhonemeDesc { Name = "ax", Desc = "Ago : mid-central unrounded vowel", Category = PhonemeCategory.Vowel } },
		{ 0x025c, new PhonemeDesc { Name = "ax2", Desc = "Ago : lower-mid central unrounded vowel", Category = PhonemeCategory.Vowel } },
		{ 0x025b, new PhonemeDesc { Name = "eh", Desc = "pEt : lower-mid front unrounded vowel", Category = PhonemeCategory.Vowel } },
		{ 0x026a, new PhonemeDesc { Name = "ih", Desc = "fIll : semi-high front unrounded vowel", Category = PhonemeCategory.Vowel } },
		{ 0x0268, new PhonemeDesc { Name = "ih2", Desc = "fIll : high central unrounded vowel", Category = PhonemeCategory.Vowel } },
		{ 0x028a, new PhonemeDesc { Name =  "uh", Desc = "bOOk : semi-high back rounded vowel", Category = PhonemeCategory.Vowel} },
		{ 'g', new PhonemeDesc { Name = "g", Desc = "taG : voiced velar stop", Category = PhonemeCategory.Stop_Plosive } },
		{ 0x0261, new PhonemeDesc { Name = "g2", Desc = "taG : voiced velar stop", Category = PhonemeCategory.Stop_Plosive } },
		{ 'h', new PhonemeDesc { Name = "hh", Desc = "Help : voiceless glottal fricative", Category = PhonemeCategory.Fricative } },
		{ 0x0266, new PhonemeDesc { Name = "hh2", Desc = "Help : breathy-voiced glottal fricative", Category = PhonemeCategory.Fricative } },
		{ 'k', new PhonemeDesc { Name = "c", Desc = "Cut : voiceless velar stop", Category = PhonemeCategory.Stop_Plosive } },
		{ 0x014b, new PhonemeDesc { Name = "nx", Desc = "siNG : voiced velar nasal", Category = PhonemeCategory.Nasal } },
		{ 0x0292, new PhonemeDesc { Name = "zh", Desc = "aZure : voiced postalveolar fricative", Category = PhonemeCategory.Fricative } }
	};
}
