/// <summary>
/// Whether props have physics while they're driven, see <see cref="Player.DrivePhysics"/>.
/// </summary>
public enum DrivePhysicsMode
{
	/// <summary>
	/// Props are all forced to be in noclip
	/// </summary>
	[Title( "Disabled" )] Disabled,

	/// <summary>
	/// Players can control if they want to use physics
	/// </summary>
	[Title( "Per-Player" )] PerPlayer,

	/// <summary>
	/// Physics are always enabled, cannot be disabled
	/// </summary>
	[Title( "Forced" )] Forced,
}

/// <summary>
/// Drive mode, control props with various modes
/// </summary>
public sealed partial class Player
{
	[Title( "Drive Props" ), Group( "Gameplay" ), ServerSettings.Section( "Props" ), Order( 23 )]
	[ConVar( "sandbox.allow.drive", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Players can drive props from the Inspector." )]
	public static bool AllowDrive { get; set; } = true;

	[Permission, Title( "Drive Props" ), Description( "Drive props when it's off for everyone." )]
	public const string DriveProps = "sandbox.drive";

	public static bool CanDriveProps( Connection connection ) => Permissions.Allows( connection, AllowDrive, DriveProps );

	[Title( "Allow Physics" ), Group( "Gameplay" ), ServerSettings.Section( "Driving" ), Order( 30 )]
	[ConVar( "sandbox.drive.physics", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Whether driven props collide and fall: Disabled, PerPlayer (players toggle it with noclip) or Forced." )]
	public static DrivePhysicsMode DrivePhysics { get; set; } = DrivePhysicsMode.PerPlayer;

	[Title( "Prop Movement Speed" ), Group( "Gameplay" ), ServerSettings.Section( "Driving" ), Order( 31 ), Range( 50, 2000 ), Step( 10 )]
	[ConVar( "sandbox.drive.speed", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "How fast driven props move, in inches per second." )]
	public static float DriveSpeed { get; set; } = 300f;

	[Title( "Sprint Multiplier" ), Group( "Gameplay" ), ServerSettings.Section( "Driving" ), Order( 32 ), Range( 1, 10 ), Step( 0.01f )]
	[ConVar( "sandbox.drive.sprint", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "How much faster driven props go while holding run." )]
	public static float DriveSprintMultiplier { get; set; } = 1.66f;

	/// <summary>
	/// The object we're driving, set by the host
	/// </summary>
	[Sync( SyncFlags.FromHost )] public GameObject Driving { get; private set; }

	/// <summary>
	/// Whether this player wants physics on what they drive, when the server leaves it up to them.
	/// </summary>
	[Sync] public bool WantsDrivePhysics { get; private set; } = true;

	/// <summary>
	/// Whether what this player drives has physics right now
	/// </summary>
	public bool DrivesWithPhysics => DrivePhysics switch
	{
		DrivePhysicsMode.Forced => true,
		DrivePhysicsMode.PerPlayer => WantsDrivePhysics,
		_ => false
	};

	/// <summary>
	/// Flying, where we want <see cref="DriveTarget"/> to be. We fly it on our machine, the host moves it there.
	/// </summary>
	[Sync] private Vector3 DrivePosition { get; set; }

	/// <summary>
	/// Flying, how we want <see cref="DriveTarget"/> turned. With physics, what the host turns it towards.
	/// </summary>
	[Sync] private Rotation DriveRotation { get; set; }

	/// <summary>
	/// With physics, how fast we want <see cref="DriveTarget"/> to go along the ground.
	/// </summary>
	[Sync] private Vector3 DriveWish { get; set; }

	/// <summary>
	/// Whether we're sending where to fly <see cref="DriveTarget"/> or which way to push it, so the host doesn't use one for the other while it hears about a switch.
	/// </summary>
	[Sync] private bool DriveFlying { get; set; }

	/// <summary>
	/// With physics, whether we've let go of the controls and left <see cref="DriveTarget"/> to physics.
	/// </summary>
	[Sync] public bool DriveHandsOff { get; private set; }

	/// <summary>
	/// The object the drive values are for, so the host doesn't move it anywhere before we've said.
	/// </summary>
	[Sync] private GameObject DriveTarget { get; set; }

	const float DriveResponseTime = 0.1f;

	private GameObject _driven;
	private GameObject _driveReleased;
	private bool _drivePhysics;
	private Vector3 _driveVelocity;
	private Angles _driveAngles;
	private float _driveDistance;
	private Vector3 _driveLocalCenter;
	private Vector3 _driveCenter;
	private Rotation _driveRotationOffset;

	/// <summary>
	/// Owner only, whether someone else had hold of what we're driving last frame.
	/// </summary>
	private bool _driveWasHeld;

	public bool IsDriving => _driven.IsValid();

	const float DriveDestroyedHoldTime = 1.5f;

	/// <summary>
	/// Owner only, where the drive camera orbited last frame, so we can keep orbiting there if what we drive is destroyed.
	/// </summary>
	private Vector3? _driveCameraPivot;

	/// <summary>
	/// Owner only, when we give the player back after what they drove was destroyed, or null if it wasn't.
	/// </summary>
	private TimeUntil? _driveHold;

	/// <summary>
	/// Whether what we drove was just destroyed and the drive camera still orbits where it was before giving the player back.
	/// </summary>
	public bool IsDriveHolding => _driveHold is { } hold && !hold && _driveCameraPivot.HasValue;

	/// <summary>
	/// Owner only, whether the driving panel is hidden. Kept between drives.
	/// </summary>
	private bool _driveHintsHidden;

	private DriveToolInfo _driveInfo;

	/// <summary>
	/// The tool panel's title and key hints while we're driving, or null if we aren't or hid the panel.
	/// </summary>
	public IToolInfo DriveInfo => IsDriving && !_driveHintsHidden ? _driveInfo ??= new DriveToolInfo( this ) : null;

	sealed class DriveToolInfo( Player player ) : IToolInfo
	{
		public bool IsValid => player.IsValid() && player.IsDriving;
		public string Name => "#drive.title";
		public string Description => null;
		public IEnumerable<ToolHint> Hints => player.GetDriveHints();
	}

	/// <summary>
	/// The controls that do something right now. Only getting out and hiding the panel while someone else holds it.
	/// </summary>
	IEnumerable<ToolHint> GetDriveHints()
	{
		if ( !(_drivePhysics && IsHeld( _driven )) )
		{
			var canToggle = DrivePhysics == DrivePhysicsMode.PerPlayer;

			if ( _drivePhysics )
			{
				yield return new( "#drive.hint.full_physics", ["attack1"] );
				yield return new( "#drive.hint.freeze", ["attack2"] );
				yield return new( "#drive.hint.jump", ["jump"] );
				if ( canToggle ) yield return new( "#drive.hint.noclip", ["noclip"] );
			}
			else
			{
				if ( canToggle ) yield return new( "#drive.hint.physics", ["noclip"] );
				yield return new( "#drive.hint.ascend", ["jump"] );
				yield return new( "#drive.hint.descend", ["duck"] );
			}
		}

		yield return new( "#drive.hint.stop", ["use"] );
		yield return new( "#drive.hint.hide", ["view"] );
	}

	public static bool CanDrive( GameObject go, Connection caller )
	{
		if ( !go.IsValid() ) return false;
		if ( go.Tags.Has( "player" ) || go.Tags.Has( "npc" ) ) return false;
		if ( go.GetComponent<Gib>().IsValid() ) return false;
		if ( !go.GetComponent<Rigidbody>().IsValid() ) return false;
		if ( go.GetComponent<DrivenProp>().IsValid() ) return false;

		return CanKeepDriving( go, caller );
	}

	public static bool CanKeepDriving( GameObject go, Connection caller ) => CanDriveProps( caller );

	public static bool IsHeld( GameObject go )
	{
		if ( !go.IsValid() ) return false;

		foreach ( var carry in go.Scene.GetAll<PlayerCarry>() )
		{
			if ( carry.Held.IsValid() && carry.Held.Root == go )
				return true;
		}

		foreach ( var physgun in go.Scene.GetAll<Physgun>() )
		{
			if ( physgun.IsGrabbing( go ) )
				return true;
		}

		return false;
	}

	internal void Drive( GameObject go )
	{
		_driveReleased = null;
		StartDriving( go );
	}

	[Rpc.Host]
	private void StartDriving( GameObject go )
	{
		if ( Rpc.Caller != Network.Owner ) return;
		if ( IsSeated ) return;
		if ( !CanDrive( go, Rpc.Caller ) || IsHeld( go ) ) return;

		ReleaseDriven();

		DrivenProp.Begin( go, this );
		Driving = go;

		Scene.RunEvent<Global.IControlEvents>( x => x.OnControlStarted( new ControlEvent { Player = this, Target = go } ) );
	}

	[Rpc.Host]
	internal void StopDriving()
	{
		if ( Rpc.Caller != Network.Owner ) return;

		ReleaseDriven();
	}

	internal void ReleaseDriven()
	{
		var driven = Driving;
		driven?.GetComponent<DrivenProp>()?.End();
		Driving = null;

		if ( driven.IsValid() )
			Scene.RunEvent<Global.IControlEvents>( x => x.OnControlStopped( new ControlEvent { Player = this, Target = driven } ) );
	}

	[Rpc.Host]
	private void DriveJump()
	{
		if ( Rpc.Caller != Network.Owner ) return;

		Driving?.GetComponent<DrivenProp>()?.Jump();
	}

	[Rpc.Host]
	private void DriveToggleFreeze()
	{
		if ( Rpc.Caller != Network.Owner ) return;

		Driving?.GetComponent<DrivenProp>()?.ToggleFreeze();
	}

	[Rpc.Host]
	private void DriveUnfreeze()
	{
		if ( Rpc.Caller != Network.Owner ) return;

		Driving?.GetComponent<DrivenProp>()?.Unfreeze();
	}

	internal bool TryGetDriveTransform( GameObject go, out Vector3 position, out Rotation rotation )
	{
		position = DrivePosition;
		rotation = DriveRotation.Normal;
		return DriveFlying && IsDriveTarget( go ) && IsFinite( position ) && IsFinite( rotation );
	}

	internal bool TryGetDriveWish( GameObject go, out Vector3 velocity, out Rotation rotation )
	{
		velocity = DriveWish.ClampLength( DriveSpeed * DriveSprintMultiplier );
		rotation = DriveRotation.Normal;
		return !DriveFlying && !DriveHandsOff && IsDriveTarget( go ) && IsFinite( velocity ) && IsFinite( rotation );
	}

	bool IsDriveTarget( GameObject go ) => go.IsValid() && DriveTarget == go && Driving == go;

	// These come from the driver's machine, don't let a bad value near the physics
	static bool IsFinite( Vector3 v ) => !v.IsNaN && !v.IsInfinity;
	static bool IsFinite( Rotation r ) => float.IsFinite( r.x ) && float.IsFinite( r.y ) && float.IsFinite( r.z ) && float.IsFinite( r.w );

	private bool ControlDriving()
	{
		if ( Driving != _driveReleased )
			_driveReleased = null;

		var target = Driving.IsValid() && Driving != _driveReleased ? Driving : null;
		if ( target != _driven )
		{
			// It was destroyed under us, rather than let go of
			if ( _driven is not null && !_driven.IsValid() && !target.IsValid() )
				_driveHold = DriveDestroyedHoldTime;

			if ( target.IsValid() )
				BeginDriving( target );
			else
				_driven = null;
		}

		if ( !_driven.IsValid() ) return ControlDriveHold();

		Controller.UseInputControls = false;
		Controller.WishVelocity = Vector3.Zero;

		if ( Input.Pressed( "use" ) )
		{
			Input.Clear( "use" );
			_driveReleased = _driven;
			_driven = null;
			StopDriving();
			return true;
		}

		// Hides the panel instead of switching our character's camera
		if ( Input.Pressed( "view" ) )
		{
			_driveHintsHidden = !_driveHintsHidden;
			Input.Clear( "view" );
		}

		// Someone else has hold of it, the controls are theirs until they let go. We can still look around.
		var held = _drivePhysics && IsHeld( _driven );

		if ( !held )
		{
			if ( Input.Pressed( "noclip" ) )
				ToggleDrivePhysics();

			// The server's setting may have changed under us too
			if ( DrivesWithPhysics != _drivePhysics )
				SetDriveMode( DrivesWithPhysics );
		}

		ControlDriveCamera();

		var speed = Input.Down( "run" ) ? DriveSpeed * DriveSprintMultiplier : DriveSpeed;

		if ( held )
		{
			DriveWish = Vector3.Zero;
			DriveHandsOff = false;
			_driveWasHeld = true;
		}
		else if ( _drivePhysics )
		{
			// Keep it turned the way they left it, rather than snapping back
			if ( _driveWasHeld )
			{
				_driveWasHeld = false;
				_driveRotationOffset = Rotation.FromYaw( _driveAngles.yaw ).Inverse * _driven.WorldRotation;
			}

			ControlDrivePhysics( speed );
		}
		else
		{
			ControlDriveFlying( speed );
		}

		// Keep use from pressing whatever our character is looking at
		Input.Clear( "use" );
		return true;
	}

	/// <summary>
	/// What we drove was destroyed, keep the player still while the camera keeps orbiting where it was.
	/// </summary>
	private bool ControlDriveHold()
	{
		if ( !IsDriveHolding || IsSeated )
		{
			_driveHold = null;
			return false;
		}

		Controller.UseInputControls = false;
		Controller.WishVelocity = Vector3.Zero;

		ControlDriveCamera();

		Input.Clear( "use" );
		return true;
	}

	/// <summary>
	/// Look around and zoom the drive camera
	/// </summary>
	private void ControlDriveCamera()
	{
		_driveAngles += Input.AnalogLook;
		_driveAngles.pitch = _driveAngles.pitch.Clamp( -89f, 89f );
		_driveAngles.roll = 0;

		if ( !Input.MouseWheel.IsNearZeroLength )
		{
			_driveDistance = (_driveDistance * (1f - Input.MouseWheel.y * 0.1f)).Clamp( 16f, 2048f );
			Inventory.ConsumeMouseWheel();
		}
	}

	/// <summary>
	/// Fly it where the camera looks. We work out where it goes, the host puts it there.
	/// </summary>
	private void ControlDriveFlying( float speed )
	{
		var wish = _driveAngles.ToRotation() * Input.AnalogMove.ClampLength( 1 );
		if ( Input.Down( "jump" ) ) wish += Vector3.Up;
		if ( Input.Down( "duck" ) ) wish += Vector3.Down;

		var fraction = 1f - MathF.Exp( -Time.Delta / DriveResponseTime );
		_driveVelocity = Vector3.Lerp( _driveVelocity, wish.ClampLength( 1 ) * speed, fraction );

		_driveCenter += _driveVelocity * Time.Delta;

		var rotation = _driveAngles.ToRotation() * _driveRotationOffset;
		DriveRotation = rotation;
		DrivePosition = _driveCenter - rotation * (_driveLocalCenter * _driven.WorldScale);

		if ( Networking.IsHost )
		{
			_driven.WorldRotation = DriveRotation;
			_driven.WorldPosition = DrivePosition;
		}
	}

	private void ControlDrivePhysics( float speed )
	{
		var yaw = Rotation.FromYaw( _driveAngles.yaw );

		DriveWish = yaw * Input.AnalogMove.WithZ( 0 ).ClampLength( 1 ) * speed;
		DriveRotation = yaw * _driveRotationOffset;

		DriveHandsOff = Input.Down( "attack1" );
		if ( Input.Pressed( "attack1" ) )
			DriveUnfreeze();
		else if ( Input.Pressed( "attack2" ) )
			DriveToggleFreeze();

		if ( Input.Pressed( "jump" ) && !DriveHandsOff )
			DriveJump();
	}

	private void ToggleDrivePhysics()
	{
		if ( DrivePhysics != DrivePhysicsMode.PerPlayer )
		{
			var phrase = DrivePhysics == DrivePhysicsMode.Forced ? "drive.physics_forced" : "drive.physics_disabled";
			Sandbox.UI.Notices.AddNotice( "block", Color.Red, Game.Language.GetPhrase( phrase ), 3 );
			return;
		}

		WantsDrivePhysics = !WantsDrivePhysics;
	}

	private void SetDriveMode( bool physics )
	{
		_drivePhysics = physics;
		_driveVelocity = Vector3.Zero;

		var rotation = _driven.WorldRotation;
		_driveCenter = _driven.WorldTransform.PointToWorld( _driveLocalCenter );
		_driveRotationOffset = physics ? Rotation.FromYaw( _driveAngles.yaw ).Inverse * rotation : _driveAngles.ToRotation().Inverse * rotation;

		DrivePosition = _driven.WorldPosition;
		DriveRotation = rotation;
		DriveWish = Vector3.Zero;
		DriveHandsOff = false;
		_driveWasHeld = false;
		DriveFlying = !physics;
		DriveTarget = _driven;
	}

	private void BeginDriving( GameObject go )
	{
		_driven = go;
		_driveHold = null;
		_driveCameraPivot = null;
		_driveAngles = Controller.EyeAngles.WithRoll( 0 );

		var bounds = GetDriveBounds( go );
		_driveLocalCenter = bounds.Center;
		_driveDistance = 64f + (bounds.Size * go.WorldScale).Length;

		SetDriveMode( DrivesWithPhysics );
	}

	/// <summary>
	/// Show the player's arms in first person while seated, so hands IK'd to a steering wheel
	/// or controls are visible. The head stays hidden.
	/// </summary>
	private void UpdateSeatedBodyVisibility()
	{
		if ( !Controller.IsValid() ) return;

		if ( IsSeated )
		{
			if ( _hideBodyBeforeSeat is null )
			{
				_hideBodyBeforeSeat = Controller.HideBodyInFirstPerson;
				_showArmsBeforeSeat = Controller.ShowArmsInFirstPerson;
				_firstPersonAnimBeforeSeat = Controller.UseFirstPersonAnimation;
			}

			Controller.HideBodyInFirstPerson = false;
			Controller.ShowArmsInFirstPerson = true;
			Controller.UseFirstPersonAnimation = false;
		}
		else if ( _hideBodyBeforeSeat is { } hideBody )
		{
			Controller.HideBodyInFirstPerson = hideBody;
			Controller.ShowArmsInFirstPerson = _showArmsBeforeSeat;
			Controller.UseFirstPersonAnimation = _firstPersonAnimBeforeSeat;
			_hideBodyBeforeSeat = null;
		}
	}

	/// <summary>
	/// Vehicles lean the driver's body over the wheel. Lean the first person camera with it so it stays in the head.
	/// </summary>
	private void ApplySeatedFirstPersonLean( ref CameraView view )
	{
		if ( Controller.ThirdPerson || _cachedSeat is not Component seat ) return;

		var vehicle = seat.GetComponentInParent<FourWheelVehicle>();
		if ( vehicle.IsValid() )
			view.Position = vehicle.LeanEyePosition( this, view.Position );
	}

	private void ApplyDriveCamera( CameraComponent camera, ref CameraView view )
	{
		if ( _driven.IsValid() )
			_driveCameraPivot = _driven.WorldTransform.PointToWorld( _driveLocalCenter );
		else if ( !IsDriveHolding || IsSeated )
			return;

		var pivot = _driveCameraPivot.Value;
		var rotation = _driveAngles.ToRotation();
		var desired = pivot + rotation.Backward * _driveDistance;

		// Don't look through walls, unless we're driving through one
		var trace = Scene.Trace.Sphere( 8f, pivot, desired ).WithTag( "world" );
		if ( _driven.IsValid() )
			trace = trace.IgnoreGameObjectHierarchy( _driven );

		var tr = trace.Run();

		view.Position = tr.Hit && !tr.StartedSolid ? tr.EndPosition : desired;
		view.Rotation = rotation;

		camera.RenderExcludeTags.Add( "firstperson" );
		camera.RenderExcludeTags.Remove( "viewer" );
	}

	private static BBox GetDriveBounds( GameObject go )
	{
		var renderer = go.GetComponentInChildren<ModelRenderer>();
		if ( renderer?.Model is not { } model )
			return BBox.FromPositionAndSize( Vector3.Zero, 16f );

		if ( renderer.GameObject == go )
			return model.Bounds;

		return model.Bounds.Transform( go.WorldTransform.ToLocal( renderer.WorldTransform ) );
	}
}
