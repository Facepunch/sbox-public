using Sandbox.Animation;

namespace Editor.AnimGraph;

/// <summary>
/// 3D preview dock that renders the graph's target model with animation.
/// </summary>
public class Preview : PreviewSceneWidget
{
	private readonly Window _window;
	private Graph _animationGraph;
	private bool _isViewModel;
	private Data _boundGraph;
	private string _boundVariationID;
	private Widget _pickerHost;
	private Layout _pickerRow;
	private FloatSlider _speedSlider;
	private ToolbarLabel _speedLabel;
	private readonly RecorderToolbar _recorder;
	private readonly PreviewOptions _options;
	private readonly PreviewToolbar _toolbar;
	private bool _ragdollActive;
	private bool _paused;
	private bool _reviewingRecording;
	private Transform _preReviewTransform;
	private int _recordedTreadmillUpdate = -1;
	private bool _showActiveAnimations;
	private float _timeScale = 1f;
	private float _fovViewModel;
	private ModelPhysics _ragdoll;
	private GameObject _ragdollFloor;
	private PreviewToolButton _ragdollToggle;
	private readonly float[] _samples = new float[120];
	private int _sampleCount;
	private int _sampleHead;
	private readonly List<(string Name, float Cycle, float Duration)> _activeAnimations = [];

	public SkinnedModelRenderer ModelRenderer => Renderer;
	public Scene PreviewScene => Scene;
	public bool IsPaused => _paused;
	public bool IsCompiling
	{
		set => SetCompiling( value );
	}

	public Preview( Window window ) : base( null, "AnimGraph Preview" )
	{
		_window = window;

		Name = "Preview";
		WindowTitle = "Preview";
		SetWindowIcon( "visibility" );
		MinimumWidth = 100;

		_options = new PreviewOptions( this, "graph" );
		_toolbar = _options.Toolbar;

		_paused = _options.Cookie( "paused", false );
		_showActiveAnimations = _options.Cookie( "activeanimations", false );
		_fovViewModel = _options.Cookie( "fov.viewmodel", 30f );

		_options.AddDisplayOption( "Active Animation Names", "movie",
			() => _showActiveAnimations,
			value => _options.SetCookie( "activeanimations", _showActiveAnimations = value ) );

		_options.CameraSettings = OpenCameraSettings;

		BuildToolbar();
		_recorder = new RecorderToolbar( window, this );
		Layout.Add( _recorder );

		_pickerHost = Layout.Add( new Widget( this ) );
		_pickerHost.Layout = _pickerRow = Layout.Row();
		_pickerRow.Margin = new Sandbox.UI.Margin( 0, 2, 0, 0 );

		SetTimeScale( _options.Cookie( "speed", 1f ) );
		SetPaused( _paused );

		RebuildModel();
	}

	private void BuildToolbar()
	{
		var bar = _toolbar.CreateBar( this, out var row );

		// Playback - pause, single step, speed
		var playback = PreviewToolbar.AddGroup( row );
		_toolbar.AddToggle( playback, "pause", "Pause", () => _paused, SetPaused );
		_toolbar.AddButton( playback, "skip_next", "Step one frame", () => { SetPaused( true ); StepFrame(); } );
		_toolbar.AddButton( playback, "replay", "Restart the graph from its initial state", RestartGraph );

		var speed = PreviewToolbar.AddGroup( row );
		_toolbar.AddButton( speed, "speed", "Playback speed - click to reset", () => SetTimeScale( 1f ) );

		_speedSlider = speed.Add( new FloatSlider( this )
		{
			Minimum = 0f,
			Maximum = 2f,
			Step = 0.05f,
			Value = 1f,
			MinimumWidth = Theme.RowHeight,
			MaximumWidth = 90,
			ToolTip = "Playback speed",
			HighlightColor = Theme.Blue,
		} );
		_speedSlider.OnValueEdited = () => SetTimeScale( _speedSlider.Value );

		_speedLabel = speed.Add( new ToolbarLabel( "1.00x" ) );

		PreviewToolbar.AddSeparator( row );

		_options.AddDisplayGroup( row );

		row.AddStretchCell();

		var camRow = row.AddRow();
		camRow.Spacing = 2;

		var simulation = PreviewToolbar.AddGroup( camRow );
		_ragdollToggle = _toolbar.AddToggle( simulation, "accessibility_new", "Ragdoll - test the model's physics", () => _ragdollActive, SetRagdollEnabled );

		// Shown by RebuildModel when the model has physics to ragdoll
		_ragdollToggle.Parent.Visible = false;

		_options.AddCameraGroup( camRow );

		Toolbar.Add( bar, 1 );
	}

	/// <summary>
	/// The orbit and viewmodel cameras keep separate fields of view - one is looking at a
	/// character, the other is a weapon held up to the screen.
	/// </summary>
	private void OpenCameraSettings( Widget button )
	{
		var viewModel = _isViewModel;
		var settings = new CameraSettings { FieldOfView = viewModel ? _fovViewModel : FieldOfView };

		var so = settings.GetSerialized();
		so.OnPropertyChanged += property =>
		{
			var fov = property.GetValue<float>();

			if ( viewModel )
				_options.SetCookie( "fov.viewmodel", _fovViewModel = fov );
			else
				_options.SetCookie( "fov", FieldOfView = fov );
		};

		PreviewOptions.OpenSettingsPopup( button, so );
	}

	private void RestartGraph()
	{
		if ( !Renderer.IsValid() )
			return;

		using ( Scene.Push() )
		{
			Renderer.Animation.Graph = null;
			Renderer.Animation.Graph = _animationGraph;
		}
	}

	private void SetPaused( bool paused )
	{
		_paused = paused;
		Scene.TimeScale = paused ? 0f : _timeScale;
		_options.SetCookie( "paused", paused );
	}

	private void SetTimeScale( float scale )
	{
		_timeScale = scale;
		if ( !_paused )
			Scene.TimeScale = scale;

		_speedSlider.Value = scale;
		_speedLabel.Text = $"{scale:0.00}x";
		_options.SetCookie( "speed", scale );
	}

	private void RebindPicker()
	{
		var variationID = _window.Graph?.SelectedVariationID;
		if ( _window.Graph == _boundGraph && variationID == _boundVariationID )
			return;

		using var _ = SuspendUpdates.For( _pickerHost );

		_boundGraph = _window.Graph;
		_boundVariationID = variationID;
		_pickerRow.Clear( true );

		if ( _boundGraph is null )
			return;

		var so = _boundGraph.GetSerialized();
		so.OnPropertyChanged += p =>
		{
			if ( p.Name == nameof( Data.TargetModel ) )
				RebuildModel();
		};
		so.OnPropertyFinishEdit += p => _window.CommitChange( $"Edit {p?.Name ?? "Graph"}" );

		var picker = ControlWidget.Create( so.GetProperty( nameof( Data.TargetModel ) ) );
		picker.MinimumHeight = 48;
		picker.MaximumHeight = 48;
		picker.HorizontalSizeMode = SizeMode.Flexible;
		_pickerRow.Add( picker, 1 );
	}

	private void SetRagdollEnabled( bool enabled )
	{
		using ( Scene.Push() )
		{
			_ragdoll?.Destroy();
			_ragdoll = null;
			_ragdollFloor?.Destroy();
			_ragdollFloor = null;
			TreadmillEnabled = true;
			_ragdollActive = false;

			if ( !enabled || !Renderer.IsValid() )
				return;

			if ( Model?.Physics is not { Parts.Count: > 0 } )
				return;

			_ragdollFloor = new GameObject( true, "floor" );
			var floor = _ragdollFloor.AddComponent<PlaneCollider>();
			floor.Static = true;
			floor.Scale = new Vector2( 8192, 8192 );

			// The treadmill cancels root motion - hand its velocity to the bodies
			var rootVelocity = Renderer.WorldRotation * Renderer.RootMotion.Position / RealTime.Delta.Clamp( 0.001f, 1f );

			_ragdoll = ModelObject.AddComponent<ModelPhysics>();
			_ragdoll.IgnoreRoot = true;
			_ragdoll.Renderer = Renderer;
			_ragdoll.Model = Model;
			_ragdoll.CopyBonesFrom( Renderer, false );

			foreach ( var body in ModelObject.GetComponentsInChildren<Rigidbody>() )
				body.Velocity += rootVelocity;

			TreadmillEnabled = false;
			_ragdollActive = true;
		}
	}

	public override void SetFocusBone( string bone )
	{
		base.SetFocusBone( bone );

		if ( _window.Graph is { } graph && graph.PreviewFocusBone != bone )
		{
			graph.PreviewFocusBone = bone;
			_window.MarkDirty( false );
		}
	}

	public void RebuildModel()
	{
		RebindPicker();

		var model = _window.Graph?.TargetModel;
		if ( Model != model )
			_recorder.Clear();

		var modelChanged = SetModel( model );

		// Ragdolling needs articulated physics - jointed bodies, not just a collision hull
		_ragdollToggle.Parent.Visible = model?.Physics is { Parts.Count: > 0, Joints.Count: > 0 };

		// Saved focus bone that exists on this model, or nothing
		var focus = _window.Graph?.PreviewFocusBone;
		FocusBone = focus is not null && Model?.Bones.GetBone( focus ) is not null ? focus : null;
		_options.RefreshFocusBone();

		// Same model - keep the live object, recreating it would pop the preview
		if ( !modelChanged )
		{
			if ( Renderer.IsValid() && Renderer.Animation.Graph != _animationGraph )
				BindAnimationGraph();

			return;
		}

		_isViewModel = model is not null && System.IO.Path.GetFileName( model.Name ).StartsWith( "v_" );

		// Camera is locked to the viewmodel's camera bone, and it isn't standing on anything
		GroundAllowed = !_isViewModel;

		if ( _options.FocusPicker.IsValid() )
			_options.FocusPicker.Visible = !_isViewModel;

		if ( !Renderer.IsValid() )
			return;

		using ( Scene.Push() )
		{
			BindAnimationGraph();

			if ( _isViewModel )
			{
				var armsGo = new GameObject( true, "arms" );
				armsGo.Parent = ModelObject;

				var arms = armsGo.AddComponent<SkinnedModelRenderer>();
				arms.Model = Model.Load( "models/first_person/first_person_arms_preview.vmdl" );
				arms.BoneMergeTarget = Renderer;
				arms.UseAnimGraph = false;
			}
		}
	}

	/// <summary>
	/// Play a compiled animation graph on the preview model. A variation is its own graph
	/// resource, so switching variation means playing a different graph. The model is rebuilt
	/// with it, so a variation never plays on another one's model.
	/// </summary>
	public void SetAnimationGraph( Graph graph )
	{
		if ( _animationGraph == graph )
			return;

		_recorder.Clear();
		_animationGraph = graph;

		RebuildModel();
	}

	private void BindAnimationGraph()
	{
		if ( !Renderer.IsValid() )
			return;

		Renderer.Sequence.Name = "";
		Renderer.UseAnimGraph = _animationGraph is not null;
		Renderer.Animation.Graph = _animationGraph;
	}

	protected override void OnScenePreFrame()
	{
		RebuildModel();

		// Model was swapped out from under an active ragdoll - tear it down
		if ( _ragdoll is not null && !_ragdoll.IsValid() )
		{
			_ragdoll = null;
			SetRagdollEnabled( false );
		}

		ApplyPreviewSettings( _window.Graph );

		// Parameters set before the graph's first tick are dropped natively, and a
		// recompile recreates the graph instance - keep pushing preview values every frame
		if ( !_reviewingRecording )
			_window.ApplyPreviewParameters();

		DrawStats();
		DrawActiveAnimations();
	}

	/// <summary>
	/// The camera is locked to the viewmodel's camera bone, so there's nothing to point anywhere.
	/// </summary>
	protected override void OnViewRightClick()
	{
		if ( _isViewModel )
			return;

		base.OnViewRightClick();
	}

	protected override void UpdateCamera()
	{
		if ( _isViewModel )
		{
			if ( Renderer.IsValid() && Renderer.TryGetBoneTransform( "camera", out var tx ) )
			{
				Camera.WorldPosition = tx.Position;
				Camera.WorldRotation = tx.Rotation;
				Camera.FieldOfView = _fovViewModel;
			}

			return;
		}

		base.UpdateCamera();
	}

	private void DrawStats()
	{
		var playing = Renderer.IsValid() && Renderer.UseAnimGraph && _animationGraph is not null;
		var ms = playing ? (float)Renderer.Animation.EvaluationTime.TotalMilliseconds : 0.0f;

		if ( !playing || ms <= 0.0f )
		{
			_sampleCount = 0;
			_sampleHead = 0;
			return;
		}

		_samples[_sampleHead] = ms;
		_sampleHead = (_sampleHead + 1) % _samples.Length;
		_sampleCount = Math.Min( _sampleCount + 1, _samples.Length );

		var total = 0.0f;
		var peak = 0.0f;

		for ( var i = 0; i < _sampleCount; i++ )
		{
			total += _samples[i];
			peak = Math.Max( peak, _samples[i] );
		}

		DrawOverlayText( $"graph {ms:0.00} ms   avg {total / _sampleCount:0.00}   peak {peak:0.00}",
			new Vector2( 6, 6 ), Color.White.WithAlpha( 0.9f ) );
	}

	private void DrawActiveAnimations()
	{
		if ( !_showActiveAnimations || !Renderer.IsValid() )
			return;

		_window.CollectActiveAnimations( Renderer.SceneModel, _activeAnimations );
		if ( _activeAnimations.Count == 0 )
			return;

		var nameWidth = Math.Max( "animation".Length, _activeAnimations.Max( animation => animation.Name.Length ) );
		var lines = _activeAnimations.Select( animation =>
			$"{animation.Name.PadRight( nameWidth )}   {animation.Cycle,5:0.00}   {animation.Cycle * animation.Duration,5:0.00}/{animation.Duration:0.00}s" );
		var header = "animation".PadRight( nameWidth ) + "   cycle          time";

		DrawOverlayText( $"{header}\n{string.Join( '\n', lines )}",
			new Vector2( 6, 24 ), Color.White.WithAlpha( 0.9f ) );
	}

	internal void ClearRecording() => _recorder.Clear();

	internal void SetRecordingReviewMode( bool reviewing, bool paused )
	{
		if ( reviewing && !_reviewingRecording && ModelObject.IsValid() )
			_preReviewTransform = ModelObject.WorldTransform;
		else if ( !reviewing && _reviewingRecording && ModelObject.IsValid() )
			ModelObject.WorldTransform = _preReviewTransform;

		if ( reviewing != _reviewingRecording )
		{
			TreadmillEnabled = !reviewing && !_ragdollActive;
			_recordedTreadmillUpdate = -1;
			ClearRootVelocity();
			Ground.Reset();
		}

		_reviewingRecording = reviewing;
		SetPaused( paused );
	}

	internal bool ApplyRecordedUpdate( int updateIndex )
	{
		var model = Renderer?.SceneModel;
		if ( !model.IsValid() )
			return false;

		var startIndex = updateIndex == _recordedTreadmillUpdate + 1 ? updateIndex : 0;
		if ( startIndex == 0 )
			Ground.Reset();

		for ( var i = startIndex; i <= updateIndex; i++ )
		{
			// Esoterica's SetUpdateToReview applies both successes without refreshing external graph debug state.
			if ( model.Animation.Recording.Seek( i ) == GraphRecording.SeekResult.Failure )
				return false;

			if ( model.Animation.Recording.TryGetCurrentUpdate( out var info )
				&& info.AdvancesTime )
			{
				Ground.Update( Renderer );
			}
		}

		_recordedTreadmillUpdate = updateIndex;
		var characterTransform = model.Animation.Recording.CharacterTransform;
		if ( model.Animation.Recording.TryGetCurrentUpdate( out var currentInfo )
			&& currentInfo.AdvancesTime
			&& currentInfo.DeltaTime > 0f )
		{
			SetRootVelocity( characterTransform.Rotation, currentInfo.DeltaTime );
		}
		else
		{
			ClearRootVelocity();
		}

		model.Update( 0f );

		if ( ModelObject.IsValid() )
			ModelObject.WorldTransform = characterTransform;

		return true;
	}

	public void Cleanup() => DestroyScene();
}
