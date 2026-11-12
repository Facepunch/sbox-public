using Sandbox;
using Sandbox.UI;
using System;
using System.Linq;
using System.Threading;

namespace MenuProject.MenuUI.Front;

/// <summary>
/// A little portrait using the Human model - yours, or someone else's dressed from the json their profile shares.
/// Hands on hips, turned a touch to the side. The front page's one can be spun and watches the cursor;
/// others just stand there.
/// </summary>
public sealed class CitizenPreview : Panel
{
	/// <summary>
	/// Show the local player, dressed as they are right now.
	/// </summary>
	public bool LocalUser { get; set; }

	/// <summary>
	/// How someone else is dressed - the json from their player overview. Ignored for <see cref="LocalUser"/>.
	/// </summary>
	public string AvatarJson
	{
		get => _avatarJson;
		set
		{
			if ( _avatarJson == value ) return;
			_avatarJson = value;
			_built = false;
		}
	}

	/// <summary>
	/// Dressed in this instead - an outfit being edited (the platform menu's Avatar page). What they've got on after is
	/// changed with <see cref="Wear"/>, which dresses the same body again rather than starting over.
	/// </summary>
	public ClothingContainer Outfit { get; set; }

	/// <summary>
	/// Lit as the avatar editor's scene lights them, rather than the portraits' sun: a key spot from in front and above,
	/// a cool rim and a warm rim from behind, a little fill, and the camera tonemapped as that scene's is
	/// </summary>
	public bool Studio { get; set; }

	/// <summary>
	/// The anim graph's idle set to stand them in, as the avatar editor's scene does ("avatar_menu") - its intro
	/// wave skipped. Null for the hands-on-hips pose the garage character used.
	/// </summary>
	public string Idle { get; set; }

	/// <summary>
	/// Stood in their pose - again after a skin swap, which rebuilds the anim graph with default parameters
	/// </summary>
	void Pose( SkinnedModelRenderer renderer )
	{
#pragma warning disable CS0618
		// Their head and body aim straight ahead - nothing points them anywhere else, only their eyes watch the cursor - but
		// the graph springs them to it, so turning them leaves them trailing round after their hips. Off, they turn as one.
		renderer.Set( "aim_head_weight", 0f );
		renderer.Set( "aim_body_weight", 0f );

		if ( string.IsNullOrEmpty( Idle ) )
		{
			renderer.Set( "special_idle_states", 1 );
			return;
		}

		renderer.Set( "b_avatar_skip_intro", true );
		renderer.Set( "special_idle_states", Idle );
#pragma warning restore CS0618
	}

	/// <summary>
	/// Let them strike the pose when they appear. Off, they're already stood in it - better for
	/// something that pops up over and over, where watching it every time looks odd.
	/// </summary>
	public bool PlayIntro { get; set; } = true;

	/// <summary>
	/// Drag or scroll to spin them, and their eyes follow the cursor while it's over them.
	/// </summary>
	public bool Interactive { get; set; }

	/// <summary>
	/// Framing - how wide the camera is, how far back, and how high (scaled by their height).
	/// </summary>
	public float FieldOfView { get; set; } = 32;
	public float CameraDistance { get; set; } = 133;
	public float CameraHeight { get; set; } = 48;

	/// <summary>
	/// Frame on their head instead - the camera sits this far above (or, negative, below) their eyes,
	/// wherever those end up. Their height setting, their hat, their pose - none of it moves them in
	/// the frame. Leave unset to use <see cref="CameraHeight"/>.
	/// </summary>
	public float? HeadOffset { get; set; }

	/// <summary>
	/// Which way they're turned to start with.
	/// </summary>
	public float Yaw { get; set; } = -25;

	ScenePanel _scene;
	SkinnedModelRenderer _renderer;
	GameObject _camera;

	/// <summary>
	/// When to line the camera up on their head - once they're dressed and stood in their pose.
	/// </summary>
	RealTimeUntil _frameHeadAt;
	bool _headFramed = true;
	string _avatarJson;
	bool _built;
	CancellationTokenSource _cancel;

	/// <summary>
	/// While skipping the intro - playing it through fast, out of sight.
	/// </summary>
	RealTimeUntil _settleUntil;
	bool _settling;

	/// <summary>
	/// Keeps the startup screen up until we've first appeared, so we don't pop in after it has faded.
	/// </summary>
	IDisposable _startupHold;
	static bool _appearedOnce;
	bool _dressed;

	/// <summary>
	/// They're hidden and the pose runs at <see cref="SettleRate"/> while they dress, then for this
	/// much longer - long enough to cover the whole intro.
	/// </summary>
	const float SettleSeconds = 0.2f;
	const float SettleRate = 20f;

	public CitizenPreview()
	{
		AddClass( "citizen-preview" );
	}

	public override void Tick()
	{
		// The first time, the startup screen waits for us to appear
		if ( !_appearedOnce && !_built && _startupHold is null && HasSomeone )
			_startupHold = MenuUtility.HoldStartupScreen( "Preparing your character" );

		base.Tick();

		if ( !_built && HasSomeone )
			Build();

		// Before revealing them, so they appear already framed
		UpdateHeadFraming();
		UpdateFrame();
		UpdateSettle();

		SetClass( "interactive", Interactive );

		if ( Interactive )
		{
			UpdateSpin();
			UpdateEyes();
		}
	}

	/// <summary>
	/// There's someone to show - an outfit, you, or someone else's json
	/// </summary>
	bool HasSomeone => Outfit is not null || LocalUser || !string.IsNullOrWhiteSpace( _avatarJson );

	CancellationTokenSource _wearCancel;

	/// <summary>
	/// Framed - by <see cref="Frame"/> or <see cref="FrameHead"/> - so the camera's eased to where that puts it
	/// </summary>
	bool _hasFrame;

	/// <summary>
	/// Where it's framed from, when it's a height and a distance (<see cref="Frame"/>)
	/// </summary>
	Vector3 _framed;

	/// <summary>
	/// How far back from their head, when it's framed on their head (<see cref="FrameHead"/>)
	/// </summary>
	float? _onHead;

	/// <summary>
	/// Where their eyes were when it was framed - taken once, not followed, or the camera would bob along with
	/// their idle
	/// </summary>
	Vector3? _eyes;

	/// <summary>
	/// Move the camera to look at them from <paramref name="height"/>, <paramref name="distance"/> back - eased there,
	/// rather than cut. For looking at the part of them being dressed.
	/// </summary>
	public void Frame( float height, float distance )
	{
		_framed = new Vector3( distance, CameraSide, height );
		_onHead = null;
		_hasFrame = true;
		_zoom = 0;
		_pan = default;
		_eyes = null;
	}

	/// <summary>
	/// Look at their head from <paramref name="distance"/> back, in the middle of the frame wherever their pose and
	/// how they're turned have put it - eased there
	/// </summary>
	public void FrameHead( float distance )
	{
		_onHead = distance;
		_hasFrame = true;
		_zoom = 0;
		_pan = default;
		_eyes = null;
	}

	/// <summary>
	/// The wheel zooms in and out instead of spinning them - straight in on what it's framed on, or their face when
	/// it's framed on their head - and dragging with the right button moves the view about. Framing them again
	/// (<see cref="Frame"/>, <see cref="FrameHead"/>) puts both back.
	/// </summary>
	public bool WheelZooms { get; set; }

	/// <summary>
	/// How far the right button's dragged the view - to the side, and up - in the world
	/// </summary>
	Vector2 _pan;

	bool _panning;
	Vector2 _lastPan;

	/// <summary>
	/// How far in the wheel's zoomed, 0 (as framed) to 1 (their face, close up)
	/// </summary>
	float _zoom;

	/// <summary>
	/// How far back zoomed all the way in on their face
	/// </summary>
	const float CloseUpDistance = 22f;

	/// <summary>
	/// How far back zoomed all the way in on anything else
	/// </summary>
	const float ZoomedDistance = 40f;

	/// <summary>
	/// How far the view can be dragged - to either side, and up or down - so they can't be lost off it
	/// </summary>
	static readonly Vector2 PanLimit = new( 30, 50 );

	/// <summary>
	/// The camera this far to the side - their pose and how they're turned leave the middle of them off where they
	/// stand, and this puts it back in the middle of the frame
	/// </summary>
	public float CameraSide { get; set; }

	/// <summary>
	/// Where their eyes are - taken the first time it's asked since they were framed. A guess at where a head is, before
	/// they're stood there to ask.
	/// </summary>
	Vector3 Eyes()
	{
		if ( _eyes is { } known ) return known;

		if ( !_dressed || !_renderer.IsValid() || _renderer.GetAttachment( "eyes" ) is not { } eyes )
			return new Vector3( 0, CameraSide, 61 );

		_eyes = eyes.Position;
		return eyes.Position;
	}

	/// <summary>
	/// Looking straight at their head from <paramref name="distance"/> back - from a little under their eyes, so their
	/// head's up in the frame, over the fade and the buttons at the bottom of it
	/// </summary>
	Vector3 OnHead( float distance )
	{
		var eyes = Eyes();
		return new Vector3( eyes.x + distance, eyes.y, eyes.z - 1.5f );
	}

	void UpdateFrame()
	{
		if ( !_hasFrame || !_camera.IsValid() ) return;

		var goal = _onHead is { } distance ? OnHead( distance ) : _framed;

		// Zoomed - on their head, in to their face; on anything else, straight in on what it's framed on
		if ( WheelZooms && _zoom > 0 )
		{
			var close = _onHead is not null ? OnHead( CloseUpDistance ) : goal.WithX( MathF.Min( goal.x, ZoomedDistance ) );
			goal = goal.LerpTo( close, _zoom );
		}

		// Dragged about with the right button - as if the view were held, it follows the pointer
		if ( _panning )
		{
			var mouse = MousePosition;
			var moved = mouse - _lastPan;
			_lastPan = mouse;

			// The world's width across the panel, where they stand - so the view keeps up with the pointer
			var across = 2 * MathF.Max( _camera.WorldPosition.x, 1 ) * MathF.Tan( FieldOfView.DegreeToRadian() * 0.5f );
			var perPixel = across / MathF.Max( Box.Rect.Width, 1 );

			_pan = new Vector2(
				Math.Clamp( _pan.x - moved.x * perPixel, -PanLimit.x, PanLimit.x ),
				Math.Clamp( _pan.y + moved.y * perPixel, -PanLimit.y, PanLimit.y ) );
		}

		goal += new Vector3( 0, _pan.x, _pan.y );

		var position = _camera.WorldPosition;
		if ( position.Distance( goal ) < 0.05f ) return;

		_camera.WorldPosition = position.LerpTo( goal, 1 - MathF.Exp( -RealTime.Delta * 8 ) );
	}

	/// <summary>
	/// Put <paramref name="shown"/> on them - the same body, dressed again, so how they're turned and framed stays.
	/// Their look (skin, height and the rest) comes from <paramref name="committed"/> when it's given, as the avatar
	/// editor has it: hovering an item shows it on, without it becoming what they are.
	/// </summary>
	public void Wear( ClothingContainer shown, ClothingContainer committed = null )
	{
		Outfit = committed ?? shown;

		// Not stood there yet - they'll be dressed in it when they are
		if ( !_dressed || !_renderer.IsValid() || _scene is null )
			return;

		_wearCancel?.Cancel();
		_wearCancel = new CancellationTokenSource();

		_ = Redress( shown, committed ?? shown, _wearCancel.Token );
	}

	async System.Threading.Tasks.Task Redress( ClothingContainer shown, ClothingContainer committed, CancellationToken token )
	{
		try
		{
			// As when they were first dressed - clothing's made in their scene
			using ( _scene.RenderScene.Push() )
			{
				var renderer = _renderer;
				var dresser = Dresser.GetOrCreate( renderer );
				dresser.UpdateAppearance( committed );
				await dresser.ApplyAsync( shown, token );

				if ( token.IsCancellationRequested || !renderer.IsValid() )
					return;

				// A skin swap rebuilds the anim graph with default parameters - the pose put back
				Pose( renderer );
			}
		}
		catch ( OperationCanceledException )
		{
			// Something else put on since
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't dress citizen preview: {e.Message}" );
		}
	}

	/// <summary>
	/// Studio lighting, for them stood at the middle facing the camera: a white key light from in front, up and off to the
	/// side, casting their shadows, and only a little flat light everywhere, so they keep their shape. The camera's
	/// tonemapped as the avatar editor scene's (avatar.scene) is.
	/// </summary>
	static void LightStudio( CameraComponent camera )
	{
		var tonemap = camera.AddComponent<Tonemapping>();
		tonemap.Mode = Tonemapping.TonemappingMode.AgX;
		tonemap.AutoExposureEnabled = false;
		tonemap.ExposureCompensation = 0f;

		var fillObject = new GameObject( true, "Fill" );
		fillObject.WorldRotation = Rotation.From( 50, 150, 0 );

		var fill = fillObject.AddComponent<DirectionalLight>();
		fill.LightColor = new Color( 0.55f, 0.6f, 0.7f ) * 0.7f;
		fill.SkyColor = new Color( 0.3f, 0.34f, 0.42f ) * 0.9f;
		fill.Shadows = false;

		var keyObject = new GameObject( true, "Key" );
		keyObject.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 42 ) - new Vector3( 120, 70, 105 ) );

		var key = keyObject.AddComponent<DirectionalLight>();
		key.LightColor = new Color( 1f, 0.97f, 0.92f ) * 1.7f;
		key.SkyColor = Color.Black;
		key.Shadows = true;
	}

	/// <summary>
	/// An outfit's copy to dress them in - dressing tidies what it's given, which isn't ours to change
	/// </summary>
	static ClothingContainer Copy( ClothingContainer outfit )
	{
		var copy = new ClothingContainer { PrefersHuman = outfit.PrefersHuman };
		copy.Deserialize( outfit.Serialize() );
		return copy;
	}

	void Build()
	{
		_built = true;

		_cancel?.Cancel();
		_cancel = new CancellationTokenSource();

		_scene?.Delete( true );
		_renderer = null;

		// Double size, scaled back down by half in the stylesheet - renders at 2x and the downscale
		// anti-aliases the model
		_scene = AddChild<ScenePanel>( "render" );

		var clothing = Outfit is not null ? Copy( Outfit ) : LocalUser ? ClothingContainer.CreateFromLocalUser() : ClothingContainer.CreateFromJson( _avatarJson );
		_yaw = Yaw;
		_dizzySpin = 0f;
		_dizzyRemaining = 0f;
		_dizzyPhase = 0f;

		_ = Dress( _scene, clothing, _cancel.Token );
	}

	async System.Threading.Tasks.Task Dress( ScenePanel panel, ClothingContainer clothing, CancellationToken token )
	{
		var scene = panel.RenderScene;

		try
		{
			// Everything - clothing included - has to be made while this scene's the active one
			using ( scene.Push() )
			{
				var cameraObject = new GameObject( true, "Camera" );
				var camera = cameraObject.AddComponent<CameraComponent>();
				camera.BackgroundColor = Color.Transparent;
				camera.FieldOfView = FieldOfView;
				camera.ZNear = 1;
				camera.ZFar = 512;

				if ( Studio )
				{
					LightStudio( camera );
				}
				else
				{
					var sunObject = new GameObject( true, "Sun" );
					sunObject.WorldRotation = Rotation.From( 50, 150, 0 );

					var sun = sunObject.AddComponent<DirectionalLight>();
					sun.LightColor = new Color( 1.0f, 0.96f, 0.9f ) * 1.6f;
					sun.SkyColor = new Color( 0.35f, 0.38f, 0.45f );
				}

				var body = new GameObject( true, "Body" );
				body.WorldRotation = Rotation.FromYaw( Yaw );

				var renderer = body.AddComponent<SkinnedModelRenderer>();
				renderer.Model = Model.Load( "models/citizen_human/human.vmdl" );

				// The hands-on-hips pose the garage character used
				Pose( renderer );

				if ( !PlayIntro )
				{
					// Hide them and run the pose through fast, so they're already in it when they show
					panel.AddClass( "settling" );
					renderer.PlaybackRate = SettleRate;
					_settling = true;
					_dressed = false;
				}

				_renderer = renderer;

				// Their clothes might not be downloaded yet - this fetches whatever's missing
				var dresser = Dresser.GetOrCreate( renderer );
				clothing.Normalize();
				dresser.UpdateAppearance( clothing );
				await dresser.ApplyAsync( clothing, token );

				if ( token.IsCancellationRequested || !cameraObject.IsValid() )
					return;

				// A human skin swaps the body model, which rebuilds the anim graph with default
				// parameters - put the pose back so the entry animation plays
				Pose( renderer );

				// Taller people need the camera a little higher - a rough guess, and all we've got unless
				// we're framing on their head, which takes over once they're posed
				var height = clothing.Height.Remap( 0, 1, 0.8f, 1.2f, true );
				cameraObject.WorldPosition = new Vector3( CameraDistance, CameraSide, CameraHeight * height );
				cameraObject.WorldRotation = Rotation.FromYaw( 180 );
				_camera = cameraObject;

				// Dressed - give the pose a moment more at speed, then show them
				_dressed = true;
				_settleUntil = SettleSeconds;

				// Shown straight away when playing the intro, so they've appeared
				if ( !_settling )
					Appeared();

				// Line up on their head once the pose has played - the same moment they're shown, or a
				// beat longer if they're striking it for real
				_headFramed = !HeadOffset.HasValue;
				_frameHeadAt = PlayIntro ? 1.0f : SettleSeconds;
			}
		}
		catch ( OperationCanceledException )
		{
			// Gone before they finished dressing
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't dress citizen preview: {e.Message}" );
		}
		finally
		{
			// However dressing ended, if they never got dressed there's nothing for the startup screen to wait for
			if ( !_dressed )
				ReleaseStartupHold();
		}
	}

	/// <summary>
	/// Put the camera where <see cref="HeadOffset"/> says, relative to their eyes. Once - following
	/// their head every frame would make the camera bob along with their idle.
	/// </summary>
	void UpdateHeadFraming()
	{
		if ( _headFramed || !_dressed || _frameHeadAt > 0 ) return;
		if ( !_renderer.IsValid() || !_camera.IsValid() || !HeadOffset.HasValue ) return;

		_headFramed = true;

		if ( _renderer.GetAttachment( "eyes" ) is not { } eyes )
			return;

		var position = _camera.WorldPosition;
		_camera.WorldPosition = position.WithZ( eyes.Position.z + HeadOffset.Value );
	}

	void Appeared()
	{
		_appearedOnce = true;
		ReleaseStartupHold();
	}

	void ReleaseStartupHold()
	{
		_startupHold?.Dispose();
		_startupHold = null;
	}

	void UpdateSettle()
	{
		if ( !_settling || !_dressed || _settleUntil > 0 ) return;

		_settling = false;
		Appeared();

		if ( _renderer.IsValid() )
			_renderer.PlaybackRate = 1;

		_scene?.RemoveClass( "settling" );
	}

	//
	// Spinning - drag to turn them, flick to send them round, scroll to shove
	//

	/// <summary>
	/// Degrees of yaw per pixel of drag.
	/// </summary>
	const float DragDegreesPerPixel = 0.6f;

	/// <summary>
	/// How quickly a flick dies off once released. Higher stops sooner.
	/// </summary>
	const float SpinFriction = 3.5f;

	/// <summary>
	/// Wheel spins coast longer so successive notches build momentum.
	/// </summary>
	const float WheelSpinFriction = 0.8f;

	/// <summary>
	/// Degrees per second added to the spin velocity per wheel notch.
	/// </summary>
	const float WheelDegreesPerNotch = 140f;

	float _yaw;
	float _yawVelocity;
	float _spinFriction = SpinFriction;
	float _lastDragX;
	bool _dragging;

	public override void OnMouseWheel( Vector2 value )
	{
		if ( !Interactive )
		{
			base.OnMouseWheel( value );
			return;
		}

		// In or out a step - towards what's under the pointer, which stays under it
		if ( WheelZooms )
		{
			ZoomAt( MousePosition, Math.Clamp( _zoom - value.y * 0.12f, 0f, 1f ) );
			return;
		}

		// Each notch gives them a shove, and a run of them stacks into a proper spin
		_spinFriction = WheelSpinFriction;
		_yawVelocity += value.y * WheelDegreesPerNotch;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( Interactive && WheelZooms && e.MouseButton == MouseButtons.Right )
		{
			_panning = true;
			_lastPan = MousePosition;
			e.StopPropagation();
			return;
		}

		if ( !Interactive || e.MouseButton != MouseButtons.Left )
			return;

		_dragging = true;
		_lastDragX = MousePosition.x;
		_yawVelocity = 0f;
		_spinFriction = SpinFriction;
	}

	/// <summary>
	/// How far the camera is from them, zoomed <paramref name="zoom"/> of the way in - from the plane they stand in
	/// </summary>
	float DepthAt( float zoom )
	{
		var head = _onHead is not null ? Eyes() : default;
		var from = _onHead is { } distance ? head.x + distance : _framed.x;
		var to = _onHead is not null ? head.x + CloseUpDistance : MathF.Min( from, ZoomedDistance );

		return from.LerpTo( to, zoom ) - head.x;
	}

	/// <summary>
	/// Zoom to <paramref name="zoom"/>, keeping what's under <paramref name="pointer"/> under it - the view moved over
	/// by as much as zooming would carry that point away from it
	/// </summary>
	void ZoomAt( Vector2 pointer, float zoom )
	{
		if ( zoom == _zoom || !_hasFrame ) return;

		var before = DepthAt( _zoom );
		var after = DepthAt( zoom );
		_zoom = zoom;

		// All the way out - back to how it's framed
		if ( zoom <= 0 )
		{
			_pan = default;
			return;
		}

		var width = MathF.Max( Box.Rect.Width, 1 );
		var perPixel = 2 * MathF.Tan( FieldOfView.DegreeToRadian() * 0.5f ) / width;
		var closer = (before - after) * perPixel;

		var fromMiddle = pointer - new Vector2( Box.Rect.Width, Box.Rect.Height ) * 0.5f;

		_pan = new Vector2(
			Math.Clamp( _pan.x + fromMiddle.x * closer, -PanLimit.x, PanLimit.x ),
			Math.Clamp( _pan.y - fromMiddle.y * closer, -PanLimit.y, PanLimit.y ) );
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		if ( e.MouseButton == MouseButtons.Right )
			_panning = false;
	}

	/// <summary>
	/// While held, yaw follows the cursor and we keep a running estimate of how fast it's moving;
	/// on release they carry on with that speed and coast to a stop.
	/// </summary>
	void UpdateSpin()
	{
		if ( !_renderer.IsValid() )
			return;

		var dt = MathF.Max( RealTime.Delta, 0.001f );
		var previousYaw = _yaw;

		if ( _dragging && !HasActive )
			_dragging = false;

		if ( _dragging )
		{
			var x = MousePosition.x;
			var deltaYaw = (x - _lastDragX) * DragDegreesPerPixel;
			_lastDragX = x;

			_yaw += deltaYaw;

			// Blend toward the instantaneous speed so a jittery frame doesn't set the fling
			_yawVelocity = _yawVelocity.LerpTo( deltaYaw / dt, 0.5f );
		}
		else
		{
			_yaw += _yawVelocity * dt;
			_yawVelocity *= MathF.Exp( -_spinFriction * dt );

			if ( MathF.Abs( _yawVelocity ) < 0.5f )
				_yawVelocity = 0f;
		}

		_renderer.GameObject.WorldRotation = Rotation.FromYaw( _yaw );

		if ( GetsDizzy )
			UpdateDizziness( MathF.Abs( _yaw - previousYaw ), dt );
	}

	/// <summary>
	/// Spun round fast enough, their eyes roll for a few seconds. Off where they're being turned to look at something.
	/// </summary>
	public bool GetsDizzy { get; set; } = true;

	/// <summary>
	/// Three quick revolutions earn a few seconds of rolling eyes. Slow turns don't count, and
	/// keeping them spinning postpones recovery until you finally leave the poor citizen alone.
	/// </summary>
	const float DizzySpinThreshold = 1080f;
	const float DizzySpinSpeed = 360f;
	const float DizzySeconds = 5f;

	float _dizzySpin;
	float _dizzyRemaining;
	float _dizzyPhase;

	void UpdateDizziness( float spin, float dt )
	{
		_dizzyRemaining = MathF.Max( 0f, _dizzyRemaining - dt );

		if ( spin / dt >= DizzySpinSpeed )
		{
			_dizzySpin = MathF.Min( DizzySpinThreshold, _dizzySpin + spin );

			if ( _dizzySpin >= DizzySpinThreshold )
				_dizzyRemaining = DizzySeconds;
		}
		else
		{
			_dizzySpin = MathF.Max( 0f, _dizzySpin - DizzySpinSpeed * dt );
		}

		if ( _dizzyRemaining > 0f )
			_dizzyPhase = (_dizzyPhase + dt * 10f) % MathF.Tau;
	}

	//
	// Eyes - they watch the cursor while it's over them
	//

	Vector3 _eyeDirection;
	float _eyeWeight;

	/// <summary>
	/// Their eyes follow the cursor wherever it is - over the rest of the page as well as them - rather than only
	/// while it's over them
	/// </summary>
	public bool WatchesCursor { get; set; }

	void UpdateEyes()
	{
		if ( !_renderer.IsValid() || _scene is null || !_scene.RenderScene.IsValid() )
			return;

		var rect = Box.Rect;
		var camera = _scene.RenderScene.Camera;

		if ( rect.Width <= 0 || rect.Height <= 0 || !camera.IsValid() )
			return;

		var eyes = _renderer.GetAttachment( "eyes" )?.Position ?? _renderer.WorldPosition + Vector3.Up * 60;

		// Default to watching the viewer - that's also what hover eases in from
		var wantDirection = (camera.WorldPosition - eyes).Normal;
		var wantWeight = 0f;

		if ( HasHovered || (WatchesCursor && Mouse.Active) )
		{
			// Cursor in panel space, centered: -0.5 .. 0.5 on both axes over them - and on out past their edges, not so
			// far that their eyes roll right round
			var u = Math.Clamp( MousePosition.x / rect.Width - 0.5f, -1.2f, 1.2f );
			var v = Math.Clamp( MousePosition.y / rect.Height - 0.5f, -1.2f, 1.2f );

			var dist = camera.WorldPosition.Distance( eyes );
			var halfWidth = dist * MathF.Tan( camera.FieldOfView.DegreeToRadian() * 0.5f );
			var halfHeight = halfWidth * (rect.Height / rect.Width);

			// Cast through the cursor pixel, but put the look target only part of the way out from
			// the camera. That keeps it well in front of their face - a target at their own depth
			// swings wildly when the cursor is near their eyes - and damps the glance to something subtle
			var ray = camera.WorldRotation.Forward * dist
				+ camera.WorldRotation.Right * (u * 2f * halfWidth)
				+ camera.WorldRotation.Up * (-v * 2f * halfHeight);

			var target = camera.WorldPosition + ray.Normal * (dist * 0.9f);

			wantDirection = (target - eyes).Normal;
			wantWeight = 1f;
		}

		if ( _dizzyRemaining > 0f )
		{
			// Roll in model space so the eyes stay dizzy whichever way they're facing. Ease back
			// into watching the cursor over the last second and a half.
			var rotation = _renderer.WorldRotation;
			var dizzyDirection = (rotation.Forward
				+ rotation.Right * (MathF.Cos( _dizzyPhase ) * 0.8f)
				+ rotation.Up * (MathF.Sin( _dizzyPhase ) * 0.65f)).Normal;
			var amount = MathF.Min( 1f, _dizzyRemaining / 1.5f );
			wantDirection = Vector3.Lerp( wantDirection, dizzyDirection, amount ).Normal;
			wantWeight = MathF.Max( wantWeight, amount );
		}

		if ( _eyeDirection.IsNearZeroLength )
			_eyeDirection = wantDirection;

		// Ease toward the target so glances read as movement, not snapping
		_eyeDirection = Vector3.Lerp( _eyeDirection, wantDirection, RealTime.Delta * 8f ).Normal;
		_eyeWeight = _eyeWeight.LerpTo( wantWeight, RealTime.Delta * 6f );

#pragma warning disable CS0618
		_renderer.SetLookDirection( "aim_eyes", _eyeDirection, _eyeWeight );
#pragma warning restore CS0618
	}

	public override void OnDeleted()
	{
		_cancel?.Cancel();
		ReleaseStartupHold();
		base.OnDeleted();
	}
}
