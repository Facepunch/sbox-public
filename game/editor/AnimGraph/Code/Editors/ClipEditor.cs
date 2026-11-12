namespace Editor.AnimGraph;

/// <summary>
/// An animation clip playing on the graph's preview model, with its own transport.
/// </summary>
internal class ClipEditor : PreviewSceneWidget
{
	private readonly ClipPreview _preview = new();
	private readonly Widget _transport;
	private readonly IconButton _playButton;
	private readonly FloatSlider _scrubber;
	private readonly Label _timeLabel;
	private readonly Label _durationLabel;
	private readonly Label _hint;

	private Model _model;
	private string _animation;
	private bool _loop = true;
	private float _speed = 1f;

	private bool _rebuild = true;
	private bool _scrubbing;
	private bool _resumeAfterScrub;
	private bool _settingPlayButton;
	private Model _previewModel;
	private string _previewAnimation;
	private bool _previewLoop;
	private float _previewSpeed;
	private Transform[] _skeletonPose;
	private IReadOnlyList<Skeleton.Bone> _skeletonBones;
	private float _sampledPosition = -1f;

	protected virtual bool AllowSkeletonPreview => false;
	public float PlaybackPosition => _preview.TimeNormalized;
	public bool IsPlaying
	{
		get => _playButton.IsActive;
		set => SetPlaying( value );
	}

	/// <summary>
	/// Where playback has got to, so a timeline alongside can line up with the pose on screen.
	/// </summary>
	public Action<float> PlayheadMoved { get; set; }

	protected virtual string NoModelHint => "Pick a model to preview this animation on.";
	protected virtual string NoAnimationHint => "Select an animation to preview it.";

	/// <summary>
	/// The graph whose preview settings - body groups, skin, attachments - the model is dressed
	/// with. Null outside the graph, where there are none.
	/// </summary>
	protected virtual Data PreviewSettings => null;

	public ClipEditor( Widget parent, string sceneName ) : base( parent, sceneName )
	{
		// Root motion is what the treadmill under the model is already showing
		Toolbar.Add( new PreviewOptions( this, AllowSkeletonPreview ? "clip-asset" : "clip",
			PreviewOption.All & ~PreviewOption.RootMotion, AllowSkeletonPreview ).CreateBar( this ), 1 );

		_hint = new Label( this )
		{
			Alignment = TextFlag.Center,
			WordWrap = true
		};
		Layout.Add( _hint );

		_transport = new Widget( this )
		{
			FixedHeight = Theme.RowHeight + 10
		};
		_transport.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SidebarBackground );
			Paint.DrawRect( _transport.LocalRect );
			return true;
		};

		var row = _transport.Layout = Layout.Row();
		row.Margin = new Sandbox.UI.Margin( 8, 5 );
		row.Spacing = 6;

		AddTransportButton( row, "first_page", "Go to start", Restart );
		AddTransportButton( row, "skip_previous", "Previous frame", () => StepFrame( -1 ) );

		_playButton = AddTransportButton( row, "play_arrow", "Play / pause", null );
		_playButton.IsToggle = true;
		_playButton.IsActive = true;
		_playButton.OnToggled = OnPlayToggled;

		AddTransportButton( row, "skip_next", "Next frame", () => StepFrame( 1 ) );
		AddTransportButton( row, "last_page", "Go to end", GoToEnd );

		_timeLabel = row.Add( CreateTimeLabel( TextFlag.RightCenter ) );

		_scrubber = row.Add( new FloatSlider( this )
		{
			Minimum = 0f,
			Maximum = 1f,
			Step = 0f,
			HighlightColor = Theme.Primary,
			ToolTip = "Animation timeline",
			HorizontalSizeMode = SizeMode.Flexible,
			SliderPaint = PaintScrubber
		}, 1 );
		_scrubber.EditingStarted = BeginScrub;
		_scrubber.EditingFinished = EndScrub;
		_scrubber.OnValueEdited = Scrub;

		_durationLabel = row.Add( CreateTimeLabel( TextFlag.LeftCenter ) );

		Layout.Add( _transport );
	}

	/// <summary>
	/// What to play, and what to play it on. Rebuilding the preview is only worth doing when the
	/// model or the clip actually changed, so looping and speed are picked up on their own.
	/// </summary>
	public void SetSource( Model model, string animation, bool loop = true, float speed = 1f )
	{
		if ( _model != model || _animation != animation )
			_rebuild = true;

		_model = model;
		_animation = animation;
		_loop = loop;
		_speed = speed;
	}

	protected void QueueRebuild() => _rebuild = true;

	internal Widget MoveTransportTo( Widget parent )
	{
		_scrubber.Visible = false;
		_timeLabel.Visible = false;
		_durationLabel.Visible = false;
		_transport.OnPaintOverride = null;
		_transport.FixedHeight = Theme.RowHeight + 4;
		_transport.Layout.Margin = 2;
		_transport.Parent = parent;
		return _transport;
	}

	/// <summary>
	/// Jump playback to a point in the clip, as a fraction of it. Pauses, since something else is
	/// driving the time now.
	/// </summary>
	public void ScrubTo( float normalized )
	{
		if ( !float.IsFinite( normalized ) )
			throw new ArgumentOutOfRangeException( nameof( normalized ) );

		if ( _rebuild )
			Rebuild();

		SetPlaying( false );
		_preview.TimeNormalized = normalized.Clamp( 0f, 1f );
	}

	private void Rebuild()
	{
		_rebuild = false;

		_previewModel = _model;
		_previewAnimation = _animation;
		_previewLoop = _loop;
		_previewSpeed = _speed;

		SetModel( _model );
		SetSkeleton( AllowSkeletonPreview && _model is null && !string.IsNullOrWhiteSpace( _animation ) ? Clip.Load( _animation )?.Skeleton : null );
		_skeletonBones = null;
		_skeletonPose = null;

		if ( !Renderer.IsValid() && PreviewSkeleton is null )
		{
			_preview.Clear();
			_hint.Text = NoModelHint;
			_hint.Visible = true;
			SetTransportEnabled( false );
			return;
		}

		if ( string.IsNullOrWhiteSpace( _animation ) )
		{
			_preview.Clear();
			_hint.Text = NoAnimationHint;
			_hint.Visible = true;
			SetTransportEnabled( false );
			return;
		}

		_preview.Looping = _loop;
		_preview.PlaybackRate = _speed;
		_preview.Play( Renderer, _animation );
		_preview.TimeNormalized = 0f;

		if ( _preview.FrameCount == 0 )
		{
			_hint.Text = "This animation isn't compiled for the target skeleton.";
			_hint.Visible = true;
			SetTransportEnabled( false );
			return;
		}

		_hint.Visible = false;
		SetTransportEnabled( _preview.FrameCount > 1 );
		SetPlaying( _playButton.IsActive );
	}

	/// <summary>
	/// Pick up whatever this editor plays from before the frame is drawn.
	/// </summary>
	protected virtual void OnPreFrame()
	{
	}

	protected override void OnScenePreFrame()
	{
		OnPreFrame();

		if ( _rebuild || _previewModel != _model || _previewAnimation != _animation )
			Rebuild();
		else
			UpdatePlaybackSettings();

		ApplyPreviewSettings( PreviewSettings );

		if ( _playButton.IsActive && !_scrubbing )
			_preview.Advance( RealTime.Delta );

		if ( PreviewSkeleton is { } skeleton && _preview.Clip is { } clip )
		{
			if ( !ReferenceEquals( _skeletonBones, skeleton.Bones ) )
			{
				_skeletonBones = skeleton.Bones;
				_skeletonPose = _skeletonBones.Select( bone => bone.Transform ).ToArray();
				_sampledPosition = -1f;
			}

			var problem = !clip.IsBound ? "Waiting for the clip's skeleton to load."
				: clip.TrackCount < _skeletonBones.Count ? "Recompile this clip against its current skeleton."
				: null;
			_hint.Text = problem ?? "";
			_hint.Visible = problem is not null;
			_transport.Enabled = problem is null && _preview.FrameCount > 1;

			if ( problem is null && _sampledPosition != _preview.TimeNormalized )
			{
				Editor.Assets.PreviewClip.SamplePose( clip, _preview.TimeNormalized, _skeletonBones, _skeletonPose );
				_sampledPosition = _preview.TimeNormalized;
			}
		}

		UpdateTransport();
	}

	protected override Transform GetSkeletonBoneTransform( int index )
		=> _skeletonPose is not null && ReferenceEquals( _skeletonBones, PreviewSkeleton.Bones )
			? _skeletonPose[index] : base.GetSkeletonBoneTransform( index );

	private static IconButton AddTransportButton( Layout row, string icon, string tooltip, Action action )
	{
		var button = new IconButton( icon, action )
		{
			ToolTip = tooltip,
			IconSize = 16,
			Background = Color.Transparent,
			BackgroundActive = Color.Transparent,
			ForegroundActive = Theme.Primary
		};

		row.Add( button );
		return button;
	}

	private static Label CreateTimeLabel( TextFlag alignment ) => new()
	{
		Text = "0:00.000",
		Color = Theme.TextControl.WithAlpha( 0.65f ),
		FixedWidth = 68,
		Margin = 5,
		Alignment = alignment
	};

	private void PaintScrubber( Rect rect, float position )
	{
		const float thumbWidth = 6f;

		var center = rect.Center.y;
		var playhead = position + thumbWidth * 0.5f;
		var track = new Rect( thumbWidth * 0.5f, center - 2f, rect.Width - thumbWidth, 4f );

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( rect, 3f );

		Paint.SetBrush( Theme.WindowBackground.WithAlpha( 0.8f ) );
		Paint.DrawRect( track, 2f );

		Paint.ClearPen();
		Paint.SetBrush( Theme.Primary.WithAlpha( 0.9f ) );
		Paint.DrawRect( new Rect( track.Left, track.Top, MathF.Max( playhead - track.Left, 0f ), track.Height ), 2f );

		PaintFrameMarkers( track, center );

		Paint.SetPen( Theme.Primary, 2f );
		Paint.DrawLine( new Vector2( playhead, 4f ), new Vector2( playhead, rect.Bottom - 4f ) );
		Paint.ClearPen();
		Paint.SetBrush( Theme.Primary );
		Paint.DrawPolygon(
			new Vector2( playhead - 4f, 3f ),
			new Vector2( playhead + 4f, 3f ),
			new Vector2( playhead, 8f ) );
	}

	private void PaintFrameMarkers( Rect track, float center )
	{
		var frameCount = _preview.FrameCount;
		if ( frameCount < 1 || track.Width <= 0f )
			return;

		const float minimumSpacing = 10f;
		var frameStep = Math.Max( 1, (int)MathF.Ceiling( frameCount * minimumSpacing / track.Width ) );

		Paint.SetPen( Theme.WindowBackground.WithAlpha( 0.9f ), 1f );
		Paint.SetBrush( Theme.TextLight.WithAlpha( 0.8f ) );
		for ( var frame = frameStep; frame < frameCount; frame += frameStep )
		{
			var x = MathX.LerpTo( track.Left, track.Right, frame / (float)frameCount );
			Paint.DrawPolygon(
				new Vector2( x, center - 3f ),
				new Vector2( x + 3f, center ),
				new Vector2( x, center + 3f ),
				new Vector2( x - 3f, center ) );
		}
	}

	private void SetTransportEnabled( bool enabled )
	{
		_transport.Enabled = enabled;

		if ( enabled )
			return;

		_scrubber.Value = 0f;
		_timeLabel.Text = FormatTime( 0f );
		_durationLabel.Text = FormatTime( 0f );
	}

	private void UpdatePlaybackSettings()
	{
		if ( _previewLoop != _loop )
		{
			_previewLoop = _loop;
			_preview.Looping = _previewLoop;
		}

		if ( _previewSpeed != _speed )
		{
			_previewSpeed = _speed;
			_preview.PlaybackRate = _previewSpeed;
		}
	}

	private void SetPlaying( bool playing )
	{
		if ( _playButton.IsActive != playing )
		{
			_settingPlayButton = true;
			_playButton.IsActive = playing;
			_settingPlayButton = false;
		}

		Scene.TimeScale = playing ? 1f : 0f;
	}

	private void OnPlayToggled( bool playing )
	{
		if ( _settingPlayButton )
			return;

		if ( playing && _preview.IsFinished )
			_preview.TimeNormalized = 0f;

		SetPlaying( playing );
	}

	private void Restart()
	{
		_preview.TimeNormalized = 0f;
	}

	private void GoToEnd()
	{
		SetPlaying( false );

		var duration = _preview.Duration;
		if ( duration <= 0f )
			return;

		_preview.Time = MathF.Max( duration - 1f / _preview.FrameRate, 0f );
	}

	private void StepFrame( int direction )
	{
		SetPlaying( false );

		var duration = _preview.Duration;
		if ( duration <= 0f )
			return;

		var frameRate = _preview.FrameRate;
		var end = MathF.Max( duration - 1f / frameRate, 0f );
		_preview.Time = (_preview.Time + direction / frameRate).Clamp( 0f, end );
	}

	private void BeginScrub()
	{
		_scrubbing = true;
		_resumeAfterScrub = _playButton.IsActive;
		SetPlaying( false );
	}

	private void Scrub()
	{
		_preview.TimeNormalized = _scrubber.Value;
	}

	private void EndScrub()
	{
		_scrubbing = false;
		SetPlaying( _resumeAfterScrub );
	}

	private void UpdateTransport()
	{
		if ( !_preview.HasClip )
			return;

		var duration = _preview.Duration;
		var time = _preview.Time.Clamp( 0f, duration );

		if ( !_scrubbing )
			_scrubber.Value = duration > 0f ? time / duration : 0f;

		PlayheadMoved?.Invoke( duration > 0f ? time / duration : 0f );

		_timeLabel.Text = FormatTime( time );
		_durationLabel.Text = FormatTime( duration );

		if ( _preview.IsFinished && _playButton.IsActive )
			SetPlaying( false );
	}

	private static string FormatTime( float seconds )
	{
		seconds = MathF.Max( seconds, 0f );
		var minutes = (int)(seconds / 60f);
		var remaining = seconds - minutes * 60f;
		return $"{minutes}:{remaining:00.000}";
	}

	/// <summary>
	/// The clip's own root motion says exactly where the model has travelled to at any point in
	/// time, so the ground is placed from that rather than integrated. Scrubbing jumps around,
	/// including backwards, which per-frame deltas can't describe.
	/// </summary>
	protected override void UpdateTreadmill()
	{
		if ( _preview.Clip is not { IsBound: true } clip )
		{
			Ground.Reset();
			return;
		}

		Ground.SetFromRootMotion( clip.GetRootMotion( _preview.TimeNormalized ) );
	}

	public override void OnDestroyed()
	{
		_preview.Dispose();
		base.OnDestroyed();
	}
}
