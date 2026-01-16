namespace Editor;

public partial class SoundPlayer : Widget
{
	private readonly TimelineView Timeline;

	public bool Playing { get; set; }
	public bool Repeating { get; set; }
	public float Time { get; private set; }

	public ToolBar ToolBar { get; private set; }

	private readonly IconButton PlayOption;

	private bool _prevPlay = false;

	public SoundPlayer( Widget parent ) : base( parent )
	{
		Name = "Timeline";
		WindowTitle = "Timeline";
		SetWindowIcon( "timeline" );

		Layout = Layout.Column();

		var header = Layout.AddRow();

		ToolBar = header.Add( new ToolBar( this ) );
		ToolBar.NoSystemBackground = false;
		ToolBar.SetIconSize( 18 );
		PlayOption = ToolBar.AddWidget( new IconButton( "play_arrow" )
		{
			ToolTip = "Play",
			IconSize = 18,
			OnClick = () => Playing = !Playing
		} );

		var timecode = header.Add( new Label( this ) );
		timecode.Bind( "Text" ).ReadOnly().From( () =>
		{
			TimeSpan t = TimeSpan.FromSeconds( Timeline.Time );
			TimeSpan d = TimeSpan.FromSeconds( Timeline.Duration );
			return $"{t.ToString( @"mm\:ss\.fff" )} / {d.ToString( @"mm\:ss\.fff" )}";
		}, null );
		timecode.Alignment = TextFlag.RightCenter;

		ToolBar.AddWidget( new IconButton( "skip_previous" )
		{
			ToolTip = "Skip to Start",
			IconSize = 18,
			OnClick = () => Timeline.MoveScrubber( 0 )
		} );
		ToolBar.AddSeparator();
		ToolBar.AddWidget( new IconButton( "repeat" )
		{
			ToolTip = "Loop",
			IconSize = 18,
			IsToggle = true,
			IsActive = Repeating,
			OnToggled = ( value ) => Repeating = value
		} );

		Timeline = Layout.Add( new TimelineView( this ), 1 );
	}

	public void Play()
	{
		Playing = true;
	}

	public void Play( float start )
	{
		Timeline.MoveScrubber( start );
		Play();
	}

	public void SetSamples( short[] samples, float duration, string sound )
	{
		Timeline.SetSamples( samples, duration, sound );
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.Antialiasing = false;
		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect );
	}

	[EditorEvent.Frame]
	protected void OnFrame()
	{
		Timeline.OnFrame();
		Time = Timeline.Time;

		PlayOption.ToolTip = Playing ? "Pause" : "Play";
		PlayOption.Icon = Playing ? "pause" : "play_arrow";

		if ( Application.FocusWidget.IsValid() )
		{
			if ( Application.IsKeyDown( KeyCode.Space ) && Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) && !_prevPlay )
			{
				Play( 0 );
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
		private readonly SoundPlayer Timeline;
		private readonly TimeAxis TimeAxis;
		private readonly Scrubber Scrubber;
		private readonly WaveForm WaveForm;

		public float ZoomLevel { get; set; } = 1.0f;
		public float Duration { get; private set; }
		public float Time { get; set; }
		public bool Scrubbing { get; set; }
		public string Sound { get; private set; }
		public SoundHandle SoundHandle { get; private set; }

		public TimelineView( SoundPlayer parent ) : base( parent )
		{
			Timeline = parent;
			SceneRect = new( 0, Size );
			HorizontalScrollbar = ScrollbarMode.Auto;
			VerticalScrollbar = ScrollbarMode.Off;
			Scale = 1;
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
			size.x -= Width - ContentRect.Width;
			SceneRect = new( 0, size );
			TimeAxis.Size = new Vector2( size.x, Theme.RowHeight );
			Scrubber.Size = new Vector2( 9, size.y );

			var r = SceneRect;
			r.Position = Vector2.Zero.WithY( Theme.RowHeight );
			r.Size = new Vector2( PositionFromTime( Duration ), Size.y - Theme.RowHeight );
			WaveForm.SceneRect = r;
			WaveForm.Analyse();

			Scrubber.Position = Scrubber.Position.WithX( PositionFromTime( Time ) ).SnapToGrid( 1.0f );
		}

		protected override void OnResize()
		{
			base.OnResize();
			DoLayout();
		}

		public override void OnDestroyed()
		{
			base.OnDestroyed();

			SoundHandle?.Stop( 0.0f );
			SoundHandle = null;
		}

		public Rect VisibleRect { get; private set; }

		public void OnFrame()
		{
			Time = Time.Clamp( 0, Duration );

			if ( !Timeline.Playing )
			{
				SoundHandle?.Stop( 0.0f );
				SoundHandle = null;
			}

			if ( Timeline.Playing && !Scrubbing )
			{
				var time = Time % Duration;
				if ( time < Time )
				{
					if ( Timeline.Repeating )
					{
						Time = time;
						if ( SoundHandle.IsValid() )
						{
							SoundHandle.Time = Time;
						}
					}
					else
					{
						SoundHandle?.Stop( 0.0f );
						time = 0;
						Time = 0;
						MoveScrubber( 0 );
						Timeline.Playing = false;
					}
				}

				if ( Timeline.Playing && !SoundHandle.IsValid() )
				{
					SoundHandle = EditorUtility.PlaySound( Sound, Time );
					SoundHandle.Time = Time;
					SoundHandle.Occlusion = false;
					SoundHandle.DistanceAttenuation = false;
				}

				Scrubber.Position = Scrubber.Position.WithX( PositionFromTime( Time ) ).SnapToGrid( 1.0f );
				if ( Timeline.Playing )
					Time += RealTime.SmoothDelta;
			}

			if ( SoundHandle.IsValid() )
			{
				SoundHandle.Paused = Scrubbing;
			}

			if ( Scrubbing || Timeline.Playing )
			{
				Translate( 1 );
				CenterOn( Scrubber.Position );
			}

			VisibleRect = Rect.FromPoints( ToScene( LocalRect.TopLeft ), ToScene( LocalRect.BottomRight ) );

			TimeAxis.Update();
			WaveForm.Update();
		}

		public float PositionFromTime( float time )
		{
			return (time / Duration).Clamp( 0, 1 ) * (Width * ZoomLevel);
		}

		public float TimeFromPosition( float position )
		{
			return (position / (Width * ZoomLevel)).Clamp( 0, 1 ) * Duration;
		}

		public void SetSamples( short[] samples, float duration, string sound )
		{
			Sound = sound;
			Duration = duration;
			WaveForm.SetSamples( samples, duration );
		}

		public void MoveScrubber( float position, bool centreOn = true )
		{
			Scrubber.Position = Vector2.Right * position.Clamp( 0, SceneRect.Width + 4 ).SnapToGrid( 1.0f );
			Time = TimeFromPosition( Scrubber.Position.x );

			if ( SoundHandle.IsValid() )
			{
				SoundHandle.Time = Time;
			}

			if ( centreOn )
			{
				CenterOn( Scrubber.Position );
				VisibleRect = Rect.FromPoints( ToScene( LocalRect.TopLeft ), ToScene( LocalRect.BottomRight ) );
			}

			WaveForm.Update();
		}

		protected override void OnWheel( WheelEvent e )
		{
			ZoomLevel *= e.Delta > 0 ? 1.1f : 0.90f;
			ZoomLevel = ZoomLevel.Clamp( 1.0f, 20.0f );

			DoLayout();

			VisibleRect = Rect.FromPoints( ToScene( LocalRect.TopLeft ), ToScene( LocalRect.BottomRight ) );

			TimeAxis.Update();
			WaveForm.Update();

			e.Accept();
		}
	}
}
