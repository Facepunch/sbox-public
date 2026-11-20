using Sandbox.Movement;

public sealed partial class Player : ICameraModifier
{
	[Property, Group( "Camera" )] public float SeatedCameraDistance { get; set; } = 200f;
	[Property, Group( "Camera" )] public float SeatedCameraHeight { get; set; } = 40f;
	[Property, Group( "Camera" )] public float SeatedCameraPositionSpeed { get; set; } = 3f;
	[Property, Group( "Camera" )] public float SeatedCameraVelocityScale { get; set; } = 0.1f;

	private ISitTarget _cachedSeat;

	public bool IsSeated => _cachedSeat is not null;

	private bool _seatCameraInitialized;
	private float _minCameraDistance;
	private float _smoothedDistance;
	private Angles _seatedAngles;
	private Vector3 _lastSeatWorldPos;
	private List<BaseSandboxWeapon> _seatedWeapons;

	private float roll;

	// The controller's first person body settings from before we sat down, restored when we get up.
	private bool? _hideBodyBeforeSeat;
	private bool _showArmsBeforeSeat;
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

		// Keep the engine's camera effects scaled by the player's screenshake preference.
		if ( CameraEffectSystem.Get( Scene ) is { } effects )
			effects.Scale = GamePreferences.Screenshake;

		camera.FovAxis = CameraComponent.Axis.Vertical;
		view.FieldOfView = Screen.CreateVerticalFieldOfView( Preferences.FieldOfView, 9.0f / 16.0f );

		if ( Controller.ThirdPerson )
			camera.RenderExcludeTags.Add( "firstperson" );
		else
			camera.RenderExcludeTags.Remove( "firstperson" );

		// Legacy game events still receive the camera - write the view out, let them run, fold back.
		BridgeCameraEvent( camera, ref view, static x => x.OnCameraSetup( _bridgeCamera ) );

		ApplyMovementCameraEffects( ref view );
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

	private void ApplyMovementCameraEffects( ref CameraView view )
	{
		if ( Controller.ThirdPerson ) return;
		if ( !GamePreferences.ViewBobbing )
		{
			roll = 0;
			return;
		}

		var r = Controller.WishVelocity.Dot( EyeTransform.Left ) / -250.0f;
		roll = MathX.Lerp( roll, r, Time.Delta * 10.0f, true );

		view.Rotation *= new Angles( 0, 0, roll * GamePreferences.ViewTilt.Clamp( 0f, 1f ) );
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

		_seatedWeapons ??= new List<BaseSandboxWeapon>();
		_seatedWeapons.Clear();

		foreach ( var obj in builder.Objects )
		{
			foreach ( var weapon in obj.GetComponentsInChildren<BaseSandboxWeapon>() )
			{
				if ( !weapon.HasOwner )
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
			if ( weapon is IPlayerControllable controllable && !controllable.CanControl( this ) ) continue;

			// The engine projects the weapon's aim point onto the screen (targeted aim resolves to a
			// camera ray, which projects to centre).
			weapon.DrawHud( Scene.Camera );
		}
	}
}
