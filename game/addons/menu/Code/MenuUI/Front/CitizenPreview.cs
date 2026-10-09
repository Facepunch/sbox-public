using Sandbox;
using Sandbox.UI;
using System;
using System.Linq;
using System.Threading;

namespace MenuProject.MenuUI.Front;

/// <summary>
/// A little portrait of a citizen - yours, or someone else's dressed from the json their profile shares.
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
		base.Tick();

		if ( !_built && (LocalUser || !string.IsNullOrWhiteSpace( _avatarJson )) )
			Build();

		// Before revealing them, so they appear already framed
		UpdateHeadFraming();
		UpdateSettle();

		SetClass( "interactive", Interactive );

		if ( Interactive )
		{
			UpdateSpin();
			UpdateEyes();
		}
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

		var clothing = LocalUser ? ClothingContainer.CreateFromLocalUser() : ClothingContainer.CreateFromJson( _avatarJson );
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

				var sunObject = new GameObject( true, "Sun" );
				sunObject.WorldRotation = Rotation.From( 50, 150, 0 );

				var sun = sunObject.AddComponent<DirectionalLight>();
				sun.LightColor = new Color( 1.0f, 0.96f, 0.9f ) * 1.6f;
				sun.SkyColor = new Color( 0.35f, 0.38f, 0.45f );

				var body = new GameObject( true, "Body" );
				body.WorldRotation = Rotation.FromYaw( Yaw );

				var renderer = body.AddComponent<SkinnedModelRenderer>();
				renderer.Model = Model.Load( clothing.PrefersHuman
					? "models/citizen_human/citizen_human_male.vmdl"
					: "models/citizen/citizen.vmdl" );

				// The hands-on-hips pose the garage character used
				renderer.Set( "special_idle_states", 1 );

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

				// Taller people need the camera a little higher - a rough guess, and all we've got unless
				// we're framing on their head, which takes over once they're posed
				var height = clothing.Height.Remap( 0, 1, 0.8f, 1.2f, true );
				cameraObject.WorldPosition = new Vector3( CameraDistance, 0, CameraHeight * height );
				cameraObject.WorldRotation = Rotation.FromYaw( 180 );
				_camera = cameraObject;

				// Dressed - give the pose a moment more at speed, then show them
				_dressed = true;
				_settleUntil = SettleSeconds;

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

	void UpdateSettle()
	{
		if ( !_settling || !_dressed || _settleUntil > 0 ) return;

		_settling = false;

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

		// Each notch gives them a shove, and a run of them stacks into a proper spin
		_spinFriction = WheelSpinFriction;
		_yawVelocity += value.y * WheelDegreesPerNotch;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( !Interactive || e.MouseButton != MouseButtons.Left )
			return;

		_dragging = true;
		_lastDragX = MousePosition.x;
		_yawVelocity = 0f;
		_spinFriction = SpinFriction;
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
		UpdateDizziness( MathF.Abs( _yaw - previousYaw ), dt );
	}

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

		if ( HasHovered )
		{
			// Cursor in panel space, centered: -0.5 .. 0.5 on both axes
			var u = MousePosition.x / rect.Width - 0.5f;
			var v = MousePosition.y / rect.Height - 0.5f;

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

		_renderer.SetLookDirection( "aim_eyes", _eyeDirection, _eyeWeight );
	}

	public override void OnDeleted()
	{
		_cancel?.Cancel();
		base.OnDeleted();
	}
}
