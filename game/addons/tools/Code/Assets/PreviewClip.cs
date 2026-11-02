using Sandbox.Animation;

namespace Editor.Assets;

[AssetPreview( "anim" )]
public class PreviewClip( Asset asset ) : AssetPreview( asset )
{
	public override float PreviewWidgetCycleSpeed => _clip is not null && _clip.Duration > 0f ? 1f / _clip.Duration : 1f;
	public override bool IsAnimatedPreview => true;

	Clip _clip;
	IReadOnlyList<Skeleton.Bone> _bones;

	// Model space transforms for the current cycle, rebuilt each frame. Parents always come
	// before their children in the bone list, so one pass is enough to accumulate them.
	Transform[] _pose;

	public float Position { get; private set; }
	public bool IsPlaying { get; set; } = true;
	public bool Looping { get; set; } = true;
	public float PlaybackRate { get; set; } = 1f;
	public Action<float> PlayheadMoved { get; set; }

	private bool _showNames = EditorCookie.Get( "clip.preview.names", false );
	private FloatSlider _scrubber;
	private Label _frame;
	private Label _status;
	private Option _play;

	public void Seek( float position )
	{
		if ( !float.IsFinite( position ) )
			throw new ArgumentOutOfRangeException( nameof( position ) );

		IsPlaying = false;
		Position = position.Clamp( 0f, 1f );
		UpdateTransport();
		PlayheadMoved?.Invoke( Position );
	}

	public void StepFrame( int direction )
	{
		if ( _clip is null || _clip.FrameCount < 2 )
			return;

		var lastFrame = _clip.FrameCount - 1;
		Seek( (MathF.Round( Position * lastFrame ) + direction) / lastFrame );
	}

	public override Widget CreateToolbar()
	{
		var transport = new Widget( null ) { Layout = Layout.Column() };
		transport.Layout.Spacing = 2;
		var bar = transport.Layout.Add( new ToolBar( transport ) );
		bar.AddOption( "Start", "first_page", () => Seek( 0f ) );
		bar.AddOption( "Previous Frame", "skip_previous", () => StepFrame( -1 ) );
		_play = bar.AddOption( "Play / Pause", "play_arrow", () =>
		{
			if ( !IsPlaying && Position >= 1f )
				Position = 0f;
			IsPlaying = !IsPlaying;
		} );
		_play.Checkable = true;
		_play.FetchCheckedState = () => IsPlaying;
		bar.AddOption( "Next Frame", "skip_next", () => StepFrame( 1 ) );
		bar.AddOption( "End", "last_page", () => Seek( 1f ) );
		bar.AddOption( "Playback Options", "more_horiz", () => OpenPlaybackMenu( transport ) );

		var row = transport.Layout.AddRow();
		row.Margin = new Sandbox.UI.Margin( 4, 0, 4, 2 );
		row.Spacing = 4;
		_scrubber = row.Add( new FloatSlider( transport )
		{
			Minimum = 0f,
			Maximum = 1f,
			Step = 0f,
			MinimumWidth = 0,
			HorizontalSizeMode = SizeMode.Flexible,
			ToolTip = "Scrub animation",
			OnValueEdited = () => Seek( _scrubber.Value ),
			EditingStarted = () => IsPlaying = false
		}, 1 );
		_frame = row.Add( new Label( transport ) { FixedWidth = 70, Alignment = TextFlag.RightCenter } );
		_status = transport.Layout.Add( new Label( transport ) { Color = Theme.Yellow, WordWrap = true } );
		UpdateTransport();
		return transport;
	}

	private void OpenPlaybackMenu( Widget parent )
	{
		var menu = new Menu( parent );
		var loop = menu.AddOption( "Loop", "repeat", () => Looping = !Looping );
		loop.Checkable = true;
		loop.Checked = Looping;
		var names = menu.AddOption( "Bone Names", "label", () =>
		{
			_showNames = !_showNames;
			EditorCookie.Set( "clip.preview.names", _showNames );
		} );
		names.Checkable = true;
		names.Checked = _showNames;
		menu.AddSeparator();
		foreach ( var speed in new[] { 0.25f, 0.5f, 1f, 2f } )
		{
			var option = menu.AddOption( $"{speed:0.##}x Speed", null, () => PlaybackRate = speed );
			option.Checkable = true;
			option.Checked = PlaybackRate == speed;
		}
		menu.OpenAtCursor();
	}

	private string PreviewProblem => _clip is null || _clip.FrameCount == 0
		? "Compile this clip to preview its animation."
		: _bones is null ? "The clip's skeleton couldn't be loaded."
		: !_clip.IsBound ? "Waiting for the clip's skeleton to load."
		: _clip.TrackCount < _bones.Count ? "Recompile this clip against its current skeleton."
		: null;

	private void UpdateTransport()
	{
		if ( _scrubber.IsValid() )
		{
			_scrubber.Value = Position;
			_scrubber.Enabled = PreviewProblem is null && _clip.FrameCount > 1;
		}
		if ( _frame.IsValid() )
			_frame.Text = $"{Position * Math.Max( (_clip?.FrameCount ?? 1) - 1, 0 ):0} / {Math.Max( (_clip?.FrameCount ?? 1) - 1, 0 )}";
		if ( _play is not null )
			_play.Enabled = PreviewProblem is null && _clip.FrameCount > 1;
		if ( _status.IsValid() )
		{
			_status.Text = PreviewProblem ?? (_clip.IsAdditive ? "Additive motion over the reference pose." : "");
			_status.Visible = !string.IsNullOrEmpty( _status.Text );
		}
	}

	public override async Task InitializeAsset()
	{
		await Task.Yield();

		_clip = Clip.Load( Asset.Path );
		if ( _clip is null || _clip.FrameCount == 0 )
			return;

		var skeleton = _clip.Skeleton;
		if ( skeleton is null || skeleton.BoneCount == 0 )
			return;

		_bones = skeleton.Bones;
		_pose = new Transform[_bones.Count];

		// Size the camera off the reference pose - sampling every frame to get a tight fit
		// isn't worth it, and the clip's skeleton isn't necessarily bound yet at this point
		var bounds = new BBox();
		foreach ( var bone in _bones )
			bounds = bounds.AddPoint( bone.Transform.Position );

		using ( Scene.Push() )
		{
			PrimaryObject = new GameObject( true, "preview animation clip" );
			PrimaryObject.WorldTransform = Transform.Zero;

			SceneSize = bounds.Size;
			SceneCenter = bounds.Center;
		}
	}

	public override void UpdateScene( float cycle, float timeStep )
	{
		// Not calling the base here - it spins PrimaryObject by the cycle, and the cycle is
		// what's driving the animation. Watching a clip play on a turntable is useless.
		using ( Scene.Push() )
		{
			if ( PrimaryObject.IsValid() )
			{
				FrameScene();
			}
		}

		TickScene( timeStep );

		if ( IsRenderingThumbnail || IsRenderingVideo )
		{
			Position = cycle - MathF.Floor( cycle );
		}
		else if ( IsPlaying && _clip is not null && _clip.Duration > 0f )
		{
			var next = Position + timeStep * PlaybackRate / _clip.Duration;
			Position = Looping ? next - MathF.Floor( next ) : next.Clamp( 0f, 1f );
			if ( !Looping && next >= 1f )
				IsPlaying = false;
		}
		UpdateTransport();
		PlayheadMoved?.Invoke( Position );

		if ( Gizmo.Settings is null || PreviewProblem is not null )
			return;

		SamplePose();
		DrawBones();
	}

	void SamplePose()
	{
		if ( _bones is null )
			return;

		for ( var i = 0; i < _bones.Count; i++ )
		{
			var local = _clip.GetLocalTransform( Position, i );
			if ( _clip.IsAdditive )
			{
				var reference = _bones[i].LocalTransform;
				// Additive channels are parent-space deltas, not ordinary transforms to compose.
				local = new Transform( reference.Position + local.Position,
					reference.Rotation * local.Rotation, reference.Scale + local.Scale );
			}
			var parent = _bones[i].ParentIndex;

			_pose[i] = parent >= 0 ? _pose[parent].ToWorld( local ) : local;
		}
	}

	void DrawBones()
	{
		if ( _bones is null || !PrimaryObject.IsValid() )
			return;

		var root = PrimaryObject.WorldTransform;

		var boneColor = Theme.Blue;
		var jointColor = Theme.Yellow;
		var nameColor = Theme.Text;

		foreach ( var bone in _bones )
		{
			var tx = root.ToWorld( _pose[bone.Index] );
			var parentTx = bone.ParentIndex >= 0 ? root.ToWorld( _pose[bone.ParentIndex] ) : root;

			using var scope = Gizmo.Scope( $"Bone{bone.Index}", tx );

			var radius = Math.Min( 1f, tx.Position.Distance( Gizmo.CameraTransform.Position ) / 64f );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.LineThickness = 1f;

			Gizmo.Draw.Color = boneColor.WithAlpha( 0.6f );
			Gizmo.Draw.Line( 0f, tx.PointToLocal( parentTx.Position ) );

			Gizmo.Draw.Color = jointColor;
			Gizmo.Draw.LineCircle( 0f, radius );

			if ( _showNames )
			{
				Gizmo.Draw.Color = nameColor;
				Gizmo.Draw.ScreenText( bone.Name, tx.Position, Vector2.Up * 8f, size: 10f, flags: TextFlag.CenterBottom );
			}
		}
	}
}
