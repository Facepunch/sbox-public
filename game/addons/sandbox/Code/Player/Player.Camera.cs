using Sandbox.Movement;

public sealed partial class Player : ICameraModifier
{
	[Property, Group( "Camera" )] public float SeatedCameraDistance { get; set; } = 200f;
	[Property, Group( "Camera" )] public float SeatedCameraHeight { get; set; } = 40f;
	[Property, Group( "Camera" )] public float SeatedCameraPositionSpeed { get; set; } = 3f;
	[Property, Group( "Camera" )] public float SeatedCameraVelocityScale { get; set; } = 0.1f;

	/// <summary>
	/// Strength of transient camera effects in Sandbox.
	/// </summary>
	internal const float ScreenShakeScale = 0.3f;

	private ISitTarget _cachedSeat;

	public bool IsSeated => _cachedSeat is not null;

	private bool _seatCameraInitialized;
	private float _minCameraDistance;
	private float _smoothedDistance;
	private Angles _seatedAngles;
	private Vector3 _lastSeatWorldPos;
	private List<BaseCombatWeapon> _seatedWeapons;

	private float _viewModelStrideDistance;
	private readonly List<(double Time, Rotation Facing)> _viewModelLookHistory = new( 32 );
	private float _airborneFallSpeed;
	private float _landingRoll;
	private float _landingRollVelocity;

	// The controller's first person body settings from before we sat down, restored when we get up.
	private bool? _showArmsBeforeSeat;
	private bool _firstPersonAnimBeforeSeat;

	// Static so the event lambdas don't capture
	private static Angles _cameraMoveAngles;
	private static CameraComponent _bridgeCamera;

	void PlayerController.IEvents.OnEyeAngles( ref Angles ang )
	{
		if ( IsDriving || IsDriveHolding )
		{
			ang = default;
			return;
		}

		_cameraMoveAngles = ang;
		Local.IPlayerEvents.Post( static x => x.OnCameraMove( ref _cameraMoveAngles ) );
		ang = _cameraMoveAngles;
	}

	int ICameraModifier.CameraOrder => 50;

	// Runs in the camera's modifier chain right after the engine PlayerController (order 0) - the
	// game's view pass: preference FOV, first person tags, movement roll and the seated camera.
	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( IsProxy || Scene.Camera != camera ) return;
		if ( !Controller.IsValid() ) return;

		if ( CameraEffectSystem.Get( Scene ) is { } effects )
			effects.Scale = ScreenShakeScale;

		camera.FovAxis = CameraComponent.Axis.Vertical;
		view.FieldOfView = Screen.CreateVerticalFieldOfView( Preferences.FieldOfView, 9.0f / 16.0f );

		if ( Controller.ThirdPerson )
			camera.RenderExcludeTags.Add( "firstperson" );
		else
			camera.RenderExcludeTags.Remove( "firstperson" );

		// Legacy game events still receive the camera - write the view out, let them run, fold back.
		BridgeCameraEvent( camera, ref view, static x => x.OnCameraSetup( _bridgeCamera ) );

		if ( _landingRoll != 0f && !Controller.ThirdPerson && GamePreferences.ViewBobbing )
			view.Rotation *= Rotation.FromRoll( _landingRoll );

		ApplySeatedCameraSetup( ref view );
		ApplySeatedFirstPersonLean( ref view );
		ApplyDriveCamera( camera, ref view );

		BridgeCameraEvent( camera, ref view, static x => x.OnCameraPostSetup( _bridgeCamera ) );
	}

	private static void BridgeCameraEvent( CameraComponent camera, ref CameraView view, Action<Local.IPlayerEvents> post )
	{
		camera.WorldPosition = view.Position;
		camera.WorldRotation = view.Rotation;
		camera.FieldOfView = view.FieldOfView;

		_bridgeCamera = camera;
		Local.IPlayerEvents.Post( post );
		_bridgeCamera = null;

		view.Position = camera.WorldPosition;
		view.Rotation = camera.WorldRotation;
		view.FieldOfView = camera.FieldOfView;
	}

	protected override void OnFixedUpdate()
	{
		if ( !IsLocalPlayer || !Controller.IsValid() ) return;

		// By the time OnLanded runs, the ground may already have stopped the body.
		if ( !Controller.IsOnGround )
			_airborneFallSpeed = Controller.Mode?.AllowFalling == true ? -Controller.Velocity.z : 0f;

		if ( _landingRoll == 0f && _landingRollVelocity == 0f ) return;
		if ( _landingRoll * _landingRoll <= 0.001f && _landingRollVelocity * _landingRollVelocity <= 0.001f )
		{
			_landingRoll = 0f;
			_landingRollVelocity = 0f;
			return;
		}

		var delta = Time.Delta;
		_landingRoll += _landingRollVelocity * delta;
		_landingRollVelocity *= MathF.Max( 0f, 1f - 9f * delta );
		_landingRollVelocity -= _landingRoll * MathF.Min( 65f * delta, 2f );
		_landingRoll = _landingRoll.Clamp( -89f, 89f );
	}

	private void StartLandingRoll()
	{
		var speed = _airborneFallSpeed;
		_airborneFallSpeed = 0f;
		if ( speed < 303f || Controller.ThirdPerson || !GamePreferences.ViewBobbing ) return;

		// A platform moving down with the player takes some of the force out of a dry landing.
		if ( GetComponent<MoveModeSwim>() is not { WaterLevel: > 0f } )
		{
			speed += MathF.Min( Controller.GroundVelocity.z, 0f );
			if ( speed < 173f ) return;
		}

		_landingRoll = speed * 0.013f;
	}

	internal (float Vertical, float Lateral) GetViewModelBob()
	{
		if ( !Controller.IsValid() || Controller.ThirdPerson || IsSeated ) return default;

		// Keep the stride on the player so switching weapons mid-step carries on from the same foot.
		const float strideLength = 288f;
		var speed = MathF.Min( Controller.Velocity.WithZ( 0 ).Length, 320f );
		_viewModelStrideDistance = (_viewModelStrideDistance + speed * Time.Delta) % strideLength;

		// The weapon rises on each step and sways sideways once per pair of steps.
		var phase = MathF.Tau * (_viewModelStrideDistance / strideLength);
		var amplitude = speed * 0.005f;
		var vertical = amplitude * (0.3f + 0.7f * MathF.Sin( phase * 2f ));
		var lateral = amplitude * (0.3f + 0.7f * MathF.Sin( phase ));
		return (vertical, lateral);
	}

	internal void ResetViewModelSway() => _viewModelLookHistory.Clear();

	internal Vector3 GetViewModelSway( Rotation facing )
	{
		var now = Time.NowDouble;
		var sampleTime = now - 0.1;
		var history = _viewModelLookHistory;

		if ( history.Count > 0 && now < history[^1].Time )
			history.Clear();

		if ( history.Count > 0 && now == history[^1].Time )
		{
			history[^1] = (now, facing);
		}
		else
		{
			history.Add( (now, facing) );
		}

		// Keep the frames either side of the sample time so the delay is the same at any frame rate.
		var first = 0;
		while ( first + 1 < history.Count && history[first + 1].Time <= sampleTime )
			first++;

		if ( first > 0 )
			history.RemoveRange( 0, first );

		var older = history[0];
		var pastFacing = older.Facing;
		if ( history.Count > 1 && sampleTime > older.Time )
		{
			var newer = history[1];
			var fraction = (float)((sampleTime - older.Time) / (newer.Time - older.Time));
			pastFacing = Rotation.Slerp( older.Facing, newer.Facing, fraction );
		}

		var turn = facing.Angles() - pastFacing.Angles();
		var offset = Vector3.Forward - turn.ToRotation().Forward;
		return facing * (offset * 1.5f);
	}

	private void UpdateSeatedWeapons()
	{
		var seat = GetComponentInParent<ISitTarget>( false );
		if ( seat is null )
		{
			_cachedSeat = null;
			_seatedWeapons = null;
			return;
		}

		if ( seat != _cachedSeat )
		{
			_cachedSeat = seat;
			_seatCameraInitialized = false;
			RebuildSeatedWeapons( (seat as Component).GameObject );
		}
	}

	private void ApplySeatedCameraSetup( ref CameraView view )
	{
		if ( !Controller.ThirdPerson )
			return;

		if ( _cachedSeat is null )
			return;

		var seatGo = (_cachedSeat as Component).GameObject;
		var seatPos = seatGo.WorldPosition + Vector3.Up * SeatedCameraHeight;

		if ( !_seatCameraInitialized )
		{
			_seatCameraInitialized = true;
			_minCameraDistance = MathF.Max( SeatedCameraDistance, RebuildContraptionBounds( seatGo ) );
			_seatedAngles = view.Rotation.Angles();
			_lastSeatWorldPos = seatPos;
			_smoothedDistance = _minCameraDistance;
		}

		_seatedAngles.yaw += Input.AnalogLook.yaw;
		_seatedAngles.pitch = (_seatedAngles.pitch + Input.AnalogLook.pitch).Clamp( -89, 89 );

		// Derive velocity from position delta and add it to the target distance
		var speed = (seatPos - _lastSeatWorldPos).Length / Time.Delta;
		_lastSeatWorldPos = seatPos;
		var targetDistance = _minCameraDistance + speed * SeatedCameraVelocityScale;

		// Smooth orbit distance
		_smoothedDistance = _smoothedDistance.LerpTo( targetDistance, Time.Delta * SeatedCameraPositionSpeed );

		// Compose rotation: yaw around world up, then pitch around local right, no gimbal lock
		var camRot = Rotation.FromYaw( _seatedAngles.yaw ) * Rotation.FromPitch( _seatedAngles.pitch );
		var desiredPos = seatPos + camRot.Backward * _smoothedDistance;

		var tr = Scene.Trace.FromTo( seatPos, desiredPos ).Radius( 8f ).WithTag( "world" ).IgnoreGameObjectHierarchy( GameObject.Root ).Run();
		var camPos = tr.Hit ? tr.HitPosition + (seatPos - desiredPos).Normal * 4f : desiredPos;

		view.Position = camPos;
		view.Rotation = Rotation.LookAt( seatPos - camPos, Vector3.Up );
	}

	private float RebuildContraptionBounds( GameObject seatGo )
	{
		var builder = new LinkedGameObjectBuilder();
		builder.AddConnected( seatGo );

		var totalBounds = new BBox();
		var initialized = false;
		foreach ( var obj in builder.Objects )
		{
			if ( obj.Tags.Has( "player" ) ) continue;
			var b = obj.GetBounds();
			totalBounds = initialized ? totalBounds.AddBBox( b ) : b;
			initialized = true;
		}

		return totalBounds.Size.Length;
	}

	private void RebuildSeatedWeapons( GameObject seatGo )
	{
		var builder = new LinkedGameObjectBuilder();
		builder.AddConnected( seatGo );

		_seatedWeapons ??= new List<BaseCombatWeapon>();
		_seatedWeapons.Clear();

		foreach ( var obj in builder.Objects )
		{
			foreach ( var weapon in obj.GetComponentsInChildren<BaseCombatWeapon>() )
			{
				if ( !weapon.Owner.IsValid() )
					_seatedWeapons.Add( weapon );
			}
		}
	}

	private void DrawSeatedWeaponHud()
	{
		if ( _seatedWeapons == null || _seatedWeapons.Count == 0 ) return;

		foreach ( var weapon in _seatedWeapons )
		{
			if ( !weapon.IsValid() ) continue;
			if ( weapon.GetComponent<IPlayerControllable>() is { } controllable && !controllable.CanControl( this ) ) continue;

			// The engine projects the weapon's aim point onto the screen (targeted aim resolves to a
			// camera ray, which projects to centre).
			weapon.DrawHud( Scene.Camera );
		}
	}
}
