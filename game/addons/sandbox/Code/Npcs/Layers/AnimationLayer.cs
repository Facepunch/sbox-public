using Sandbox.Citizen;

namespace Sandbox.Npcs.Layers;

/// <summary>
/// Provides animation parameters and helpers for behaviors.
/// Also handles look-at (eyes/head) and body turning via animator parameters.
/// Synced properties replicate animation state to all clients.
/// </summary>
public sealed partial class AnimationLayer : BaseNpcLayer
{
	public float Speed { get; set; } = 1.0f;
	public bool IsGrounded => Npc.IsOnGround;

	/// <summary>
	/// How fast the body turns to face a look target, in degrees per second.
	/// Constant rate - an exponential turn covers most of a half-circle in its
	/// first moments, which reads as a jolting spin.
	/// </summary>
	public float BodyTurnSpeed { get; set; } = 120f;

	public float MaxHeadAngle { get; set; } = 45f;

	public float AimStrengthEyes { get; set; } = 1.0f;
	public float AimStrengthHead { get; set; } = 1.0f;
	public float AimStrengthBody { get; set; } = 1.0f;

	/// <summary>How far the head can aim away from the body, in degrees of yaw.</summary>
	public float MaxAimYaw { get; set; } = 80f;

	/// <summary>How quickly the head sweeps onto (and lets go of) a look target.</summary>
	public float AimSmoothSpeed { get; set; } = 6f;

	/// <summary>Play footstep sounds from the model's footstep animation events.</summary>
	[Property]
	public bool EnableFootsteps { get; set; } = true;

	/// <summary>Overall footstep volume multiplier.</summary>
	[Property]
	public float FootstepVolume { get; set; } = 1f;

	private TimeSince _timeSinceStep;

	/// <summary>
	/// Current world-space target the Npc is looking at (if any). Host-only.
	/// </summary>
	public Vector3? LookTarget { get; private set; }

	/// <summary>
	/// The GameObject being tracked as the look target, if any. Host-only.
	/// </summary>
	public GameObject LookTargetObject { get; private set; }

	// A temporary look target that overrides the persistent one until it expires,
	// like HL2's AddLookTarget. Speech uses this to look whoever we're talking to
	// in the eyes for the duration of the line.
	private GameObject _addedLookTarget;
	[Sync( SyncFlags.FromHost )]
	private TimeUntil _addedLookExpires { get; set; }

	private SkinnedModelRenderer _renderer => Npc.IsValid() ? Npc.Renderer : null;
	private float _lastYaw = float.NaN;
	private bool _turningToTarget;

	// The aim we're actually feeding the animgraph. Look targets appear out of
	// nowhere (senses noticing someone) and jump around; the neck eases toward
	// them and releases the same way, instead of teleporting.
	private Vector3 _aimDirection;
	private float _aimWeight;

	// Which side the head swung for a target directly behind us. Straight behind,
	// the shortest way round is ambiguous and noise flips it left/right every
	// frame -- pick a side and stay on it until the target comes back around.
	private int _aimSide;

	[Sync] public Vector3 MoveVelocity { get; set; }
	[Sync] public Rotation MoveRotation { get; set; }
	[Sync] public bool Grounded { get; set; } = true;
	[Sync] public Vector3 LookWorldPos { get; set; }
	[Sync] public bool IsLooking { get; set; }
	[Sync] public string HoldType { get; set; } = "none";

	/// <summary>
	/// Hold the weapon lowered, at ease, instead of up and ready. Synced to all clients.
	/// </summary>
	[Sync] public bool WeaponLowered { get; set; }

	// The object being looked at, if the look target is an object. Synced so every
	// client resolves the eye position locally - that way the player being looked
	// at sees the NPC looking at their camera, while everyone else sees it looking
	// at that player's avatar's eyes.
	[Sync] public GameObject LookObject { get; set; }

	protected override void OnEnabled()
	{
		_lastYaw = float.NaN;

		// Footstep animation events fire on every client, so each hears them locally (like the player).
		if ( _renderer.IsValid() )
		{
			_renderer.OnFootstepEvent -= OnFootstepEvent;
			_renderer.OnFootstepEvent += OnFootstepEvent;
		}
	}

	protected override void OnDisabled()
	{
		if ( _renderer.IsValid() )
			_renderer.OnFootstepEvent -= OnFootstepEvent;
	}

	// Play a footstep when the model's animation hits a footstep event -- same as the player:
	// find the surface underfoot and play its footstep sound, scaled by speed.
	private void OnFootstepEvent( SceneModel.FootstepEvent e )
	{
		if ( !EnableFootsteps ) return;
		if ( _timeSinceStep < 0.2f ) return;

		var volume = e.Volume * MoveVelocity.Length.Remap( 0, 400, 0, 1 );
		if ( volume <= 0.1f ) return;

		_timeSinceStep = 0;
		PlayFootstepSound( e.Transform.Position, volume, e.FootId );
	}

	private void PlayFootstepSound( Vector3 position, float volume, int foot )
	{
		// null when the ground trace missed, e.g. mid-air
		var surface = Npc.GroundSurface;
		if ( surface is null )
			return;

		var soundEvent = foot == 0
			? surface.SoundCollection.FootLeft
			: surface.SoundCollection.FootRight;

		if ( soundEvent is null )
			return;

		var handle = GameObject.PlaySound( soundEvent, 0 );
		if ( !handle.IsValid() )
			return;

		handle.FollowParent = false;
		handle.Volume *= volume * FootstepVolume;
	}

	protected override void OnUpdate()
	{
		if ( !IsProxy )
		{
			// A timed look target wins while it's active (eg speech looking at
			// whoever we're talking to), then we fall back to the persistent one
			LookObject = _addedLookTarget.IsValid() && !_addedLookExpires
				? _addedLookTarget
				: LookTargetObject;

			if ( LookTargetObject.IsValid() )
				LookTarget = GetEyePosition( LookTargetObject );

			var lookPos = LookObject.IsValid() ? (Vector3?)GetEyePosition( LookObject ) : LookTarget;

			// Held or thrown - let go of whatever we were looking at, or the head and upper body keep
			// twisting back toward it while the body gets turned around
			if ( Npc.IsAirborne )
			{
				LookObject = null;
				lookPos = null;
			}

			IsLooking = lookPos.HasValue;
			if ( lookPos.HasValue )
			{
				LookWorldPos = lookPos.Value;
				UpdateLookDirection( lookPos.Value );
			}
			else
			{
				// No target - release the head, or it stays stuck wherever the
				// last glance left it
				ClearAim();
			}
		}
		else
		{
			// Resolve object targets locally, so this client's own view of the
			// target (first person camera vs avatar eyes) is what gets looked at
			if ( LookObject.IsValid() )
				ApplyLookToRenderer( GetEyePosition( LookObject ) );
			else if ( IsLooking )
				ApplyLookToRenderer( LookWorldPos );
			else
				ClearAim();
		}

		ApplyMoveToRenderer( MoveVelocity, MoveRotation );

		if ( !string.IsNullOrEmpty( HoldType ) )
			_renderer?.Set( "holdtype", HoldType );

		_renderer?.Set( "b_weapon_lower", WeaponLowered );

		_renderer?.Set( "b_grounded", Grounded );

		if ( DebugJitter )
			DrawJitterDebug();
	}

	/// <summary>
	/// Shows what's feeding the head every frame, to hunt down rhythmic jitter.
	/// Watch which value pulses: body yaw (someone rotating the object), aim
	/// (look target moving or flickering), or move params (animgraph input).
	/// </summary>
	[ConVar( "sandbox.npc.debug.jitter" )]
	public static bool DebugJitter { get; set; }

	float _dbgLastBodyYaw = float.NaN;
	Vector3 _dbgLastAimDir;
	bool _dbgLastHadAim;
	TimeSince _dbgLastYawSpike;
	TimeSince _dbgLastAimSpike;
	TimeSince _dbgLastAimToggle;

	private void DrawJitterDebug()
	{
		if ( !Npc.IsValid() || !_renderer.IsValid() )
			return;

		var camera = Npc.Scene.Camera;
		if ( !camera.IsValid() ) return;

		// Body rotation changes
		var yaw = Npc.WorldRotation.Angles().yaw;
		var yawDelta = float.IsNaN( _dbgLastBodyYaw ) ? 0f : MathF.Abs( Angles.NormalizeAngle( yaw - _dbgLastBodyYaw ) );
		_dbgLastBodyYaw = yaw;
		if ( yawDelta > 0.1f ) _dbgLastYawSpike = 0;

		// Aim direction changes and set/released flicker
		var hasAim = LookObject.IsValid() || IsLooking;
		var aimDir = hasAim ? (LookWorldPos - Npc.WorldPosition).Normal : Npc.WorldRotation.Forward;
		var aimDelta = Vector3.GetAngle( _dbgLastAimDir, aimDir );
		_dbgLastAimDir = aimDir;
		if ( aimDelta > 1f ) _dbgLastAimSpike = 0;
		if ( hasAim != _dbgLastHadAim ) _dbgLastAimToggle = 0;
		_dbgLastHadAim = hasAim;

		var worldPos = Npc.WorldPosition + Vector3.Up * 90f;
		var screenPos = camera.PointToScreenPixels( worldPos, out var behind );
		if ( behind ) return;

		var text = TextRendering.Scope.Default;
		text.Text =
			$"yaw {yaw:F1}  d {yawDelta:F2}  spike {_dbgLastYawSpike.Relative:F2}s ago\n" +
			$"aim {(hasAim ? "on" : "off")}  d {aimDelta:F2}  spike {_dbgLastAimSpike.Relative:F2}s ago  toggle {_dbgLastAimToggle.Relative:F1}s ago\n" +
			$"vel {MoveVelocity.Length:F1}  turning {_turningToTarget}";
		text.FontSize = 12;
		text.TextColor = Color.Yellow;

		Npc.DebugOverlay.ScreenText( screenPos, text, TextFlag.CenterBottom );
	}

	/// <summary>
	/// Look at a target for a limited time, overriding the persistent look target.
	/// The NPC aims at the target's eyes. Call again to extend - when it expires we
	/// fall back to the persistent target, if any.
	/// </summary>
	public void AddLookTarget( GameObject target, float duration )
	{
		_addedLookTarget = target;
		_addedLookExpires = duration;
	}

	/// <summary>
	/// Where a GameObject's eyes are - the "eyes" attachment if it has a model with
	/// one (players, NPCs), otherwise roughly head height above its position.
	/// The local player in first person is really "at" their camera, so on their
	/// own screen we look straight down the lens.
	/// </summary>
	public static Vector3 GetEyePosition( GameObject go )
	{
		if ( !go.IsValid() )
			return default;

		var controller = go.GetComponentInChildren<PlayerController>();
		if ( controller.IsValid() && !controller.IsProxy && !controller.ThirdPerson && go.Scene?.Camera.IsValid() == true )
			return go.Scene.Camera.WorldPosition;

		var renderer = go.GetComponentInChildren<SkinnedModelRenderer>();
		if ( renderer.IsValid() && renderer.GetAttachment( "eyes" ) is { } eyes )
			return eyes.Position;

		return go.WorldPosition + Vector3.Up * 60f;
	}

	/// <summary>
	/// Where this NPC looks from.
	/// </summary>
	private Vector3 GetOwnEyePosition()
	{
		if ( _renderer.IsValid() && _renderer.GetAttachment( "eyes" ) is { } eyes )
			return eyes.Position;

		return Npc.WorldPosition + Vector3.Up * 60f;
	}

	/// <summary>
	/// Set a persistent look target that tracks a GameObject each frame.
	/// </summary>
	public void SetLookTarget( GameObject target )
	{
		LookTargetObject = target;
		LookTarget = target.IsValid() ? target.WorldPosition : null;
	}

	/// <summary>
	/// Set a persistent look target at a fixed world position.
	/// </summary>
	public void SetLookTarget( Vector3 target )
	{
		LookTargetObject = null;
		LookTarget = target;
	}

	/// <summary>
	/// Clear the persistent look target. The NPC will stop tracking.
	/// </summary>
	public void ClearLookTarget()
	{
		LookTargetObject = null;
		LookTarget = null;
		LookObject = null;
		IsLooking = false;

		ClearAim();
	}

	// Release the aim back to the animation - ease the weight off toward body
	// forward rather than dropping it, so the head settles instead of popping.
	// Called every frame while there's no target, which is what drives the ease.
	private void ClearAim()
	{
		if ( _aimWeight <= 0.01f )
		{
			ResetAim();
			return;
		}

		if ( Npc.IsValid() )
			ApplyAim( Npc.WorldRotation.Forward, 0f );
	}

	// Hard-drop the aim with no easing. Uses body forward rather than a zero
	// vector - zero isn't a direction, and the graph does strange things with it.
	private void ResetAim()
	{
		_aimWeight = 0f;
		_aimSide = 0;

		if ( !_renderer.IsValid() || !Npc.IsValid() )
			return;

		var forward = Npc.WorldRotation.Forward;
		_aimDirection = forward;

		_renderer.SetLookDirection( "aim_eyes", forward, 0f );
		_renderer.SetLookDirection( "aim_head", forward, 0f );
		_renderer.SetLookDirection( "aim_body", forward, 0f );
	}

	/// <summary>
	/// Ease the applied aim toward a desired direction and weight, clamped to what
	/// a neck can actually do. Every look path feeds through here, so the head
	/// sweeps onto targets instead of snapping, and never flip-flops on a target
	/// directly behind us.
	/// </summary>
	private void ApplyAim( Vector3 desiredDirection, float desiredWeight )
	{
		if ( !_renderer.IsValid() || !Npc.IsValid() )
			return;

		desiredDirection = ClampAimYaw( desiredDirection );

		// Starting to look from rest - sweep out from where the head naturally sits
		if ( _aimWeight <= 0.01f && desiredWeight > 0f )
			_aimDirection = Npc.WorldRotation.Forward;

		var t = 1f - MathF.Exp( -AimSmoothSpeed * Time.Delta );
		_aimDirection = Vector3.Slerp( _aimDirection, desiredDirection, t, clamp: false ).Normal;
		_aimWeight = _aimWeight.LerpTo( desiredWeight, t );

		_renderer.SetLookDirection( "aim_eyes", _aimDirection, AimStrengthEyes * _aimWeight );
		_renderer.SetLookDirection( "aim_head", _aimDirection, AimStrengthHead * _aimWeight );
		_renderer.SetLookDirection( "aim_body", _aimDirection, AimStrengthBody * _aimWeight );
	}

	// Keep the aim within reach of the neck. A target beyond MaxAimYaw pins the
	// head at the limit on that side while the body turns to catch up; directly
	// behind, the side is ambiguous, so we stick with the one we already chose.
	private Vector3 ClampAimYaw( Vector3 direction )
	{
		var forward = Npc.WorldRotation.Forward.WithZ( 0 ).Normal;
		var flat = direction.WithZ( 0 );

		if ( flat.Length < 0.001f )
			return direction;

		var flatNormal = flat.Normal;
		var yaw = MathF.Atan2( forward.Cross( flatNormal ).z, forward.Dot( flatNormal ) ).RadianToDegree();

		if ( MathF.Abs( yaw ) > 150f )
		{
			if ( _aimSide == 0 )
				_aimSide = yaw >= 0f ? 1 : -1;

			yaw = _aimSide * MaxAimYaw;
		}
		else
		{
			_aimSide = yaw >= 0f ? 1 : -1;
			yaw = Math.Clamp( yaw, -MaxAimYaw, MaxAimYaw );
		}

		var clampedFlat = Rotation.FromAxis( Vector3.Up, yaw ) * forward * flat.Length;
		return (clampedFlat + Vector3.Up * direction.z).Normal;
	}

	/// <summary>
	/// Command this layer to look at a target (one-shot, no tracking).
	/// </summary>
	public void LookAt( Vector3 target ) => LookTarget = target;

	/// <summary>Stop looking.</summary>
	public void StopLooking() => ClearLookTarget();

	/// <summary>
	/// Returns true if the NPC body is facing the current look target within MaxHeadAngle.
	/// </summary>
	public bool IsFacingTarget()
	{
		if ( !LookTarget.HasValue ) return true;
		if ( _renderer is null ) return true;

		var direction = (LookTarget.Value.WithZ( 0 ) - Npc.WorldPosition.WithZ( 0 )).Normal;
		var angleToTarget = Vector3.GetAngle( Npc.WorldRotation.Forward.WithZ( 0 ), direction );
		return angleToTarget <= MaxHeadAngle;
	}

	private void UpdateLookDirection( Vector3 targetPosition )
	{
		if ( _renderer is null ) return;

		// Aim eyes and head from our eyes, so we meet the target's gaze rather
		// than tilting at them from our feet
		var fullDirection = (targetPosition - GetOwnEyePosition()).Normal;
		var flatDirection = (targetPosition - Npc.WorldPosition).WithZ( 0 ).Normal;

		ApplyAim( fullDirection, 1f );

		// While travelling, NavigationLayer faces the body along the movement direction, so the
		// look-at just tracks with the head/eyes -- turning the body too would make it run
		// sideways. Standing still (or strafing, in combat) the body is ours to turn.
		if ( Npc.Navigation.IsValid() && Npc.Navigation.FaceMovementDirection && Npc.Navigation.IsMoving )
			return;

		var angleToTarget = Vector3.GetAngle( Npc.WorldRotation.Forward, flatDirection );

		// Hysteresis: start turning when the target is beyond what the head can
		// reach, and keep turning until we're comfortably facing it. If we stop
		// exactly at the threshold, the turn-in-place animation nudges the body
		// back across it and we shuffle-step forever - a rhythmic head jitter.
		if ( angleToTarget > MaxHeadAngle )
			_turningToTarget = true;
		else if ( angleToTarget < MaxHeadAngle * 0.5f )
			_turningToTarget = false;

		if ( _turningToTarget )
		{
			var targetRotation = Rotation.LookAt( flatDirection, Vector3.Up );
			var remaining = Npc.WorldRotation.Distance( targetRotation );

			if ( remaining > 0.1f )
			{
				var step = MathF.Min( 1f, BodyTurnSpeed * Time.Delta / remaining );
				Npc.GameObject.WorldRotation = Rotation.Slerp( Npc.WorldRotation, targetRotation, step );
			}
		}
	}

	private void ApplyLookToRenderer( Vector3 lookWorldPos )
	{
		if ( !_renderer.IsValid() || !Npc.IsValid() ) return;

		var fullDirection = (lookWorldPos - GetOwnEyePosition()).Normal;

		ApplyAim( fullDirection, 1f );
	}

	public void SetAim( Vector3 direction )
	{
		_renderer?.SetLookDirection( "aim_eyes", direction, AimStrengthEyes );
		_renderer?.SetLookDirection( "aim_head", direction, AimStrengthHead );
		_renderer?.SetLookDirection( "aim_body", direction, AimStrengthBody );
	}

	public void SetHead( Vector3 direction ) => _renderer?.SetLookDirection( "aim_head", direction, AimStrengthHead );
	public void SetEyes( Vector3 direction ) => _renderer?.SetLookDirection( "aim_eyes", direction, AimStrengthEyes );

	/// <summary>
	/// Records move state for replication. Called by NavigationLayer on the host.
	/// All clients apply this each frame in OnUpdate.
	/// </summary>
	public void SetMove( Vector3 velocity, Rotation reference )
	{
		MoveVelocity = velocity;
		MoveRotation = reference;
	}

	private void ApplyMoveToRenderer( Vector3 velocity, Rotation reference )
	{
		if ( _renderer is null ) return;
		if ( reference.w == 0f ) return;

		var forward = reference.Forward.Dot( velocity );
		var sideward = reference.Right.Dot( velocity );
		var angle = MathF.Atan2( sideward, forward ).RadianToDegree().NormalizeDegrees();

		var yaw = reference.Angles().yaw.NormalizeDegrees();
		float rotationSpeed = 0f;

		if ( float.IsNaN( _lastYaw ) )
		{
			_lastYaw = yaw;
		}
		else
		{
			var deltaYaw = Angles.NormalizeAngle( yaw - _lastYaw );
			rotationSpeed = Time.Delta > 0f ? MathF.Abs( deltaYaw ) / Time.Delta : 0f;
			_lastYaw = yaw;
		}

		_renderer.Set( "move_direction", angle );
		_renderer.Set( "move_speed", velocity.Length );
		_renderer.Set( "move_groundspeed", velocity.WithZ( 0 ).Length );
		_renderer.Set( "move_y", sideward );
		_renderer.Set( "move_x", forward );
		_renderer.Set( "move_z", velocity.z );
		_renderer.Set( "speed_move", Speed );
		_renderer.Set( "move_rotationspeed", rotationSpeed );

		_renderer.Set( "wish_groundspeed", velocity.WithZ( 0f ).Length );
		_renderer.Set( "wish_x", forward );
		_renderer.Set( "wish_y", sideward );
		_renderer.Set( "wish_z", velocity.z );
	}

	/// <summary>
	/// Broadcasts the attack trigger to all clients so the animation plays everywhere.
	/// </summary>
	[Rpc.Broadcast]
	public void TriggerAttack()
	{
		_renderer?.Set( "b_attack", true );
	}

	/// <summary>
	/// Sets the holdtype so the NPC poses its arms for the held item - an option name on the
	/// animgraph's holdtype enum (e.g. "pistol"). Synced to all clients via HoldType.
	/// </summary>
	public void SetHoldType( string holdType )
	{
		HoldType = string.IsNullOrEmpty( holdType ) ? "none" : holdType;
	}

	public override void ResetLayer()
	{
		if ( _renderer is null ) return;

		Speed = 1.0f;
		LookTarget = null;
		LookTargetObject = null;
		LookObject = null;
		IsLooking = false;
		MoveVelocity = default;
		HoldType = "none";
		WeaponLowered = false;
		_lastYaw = float.NaN;

		_renderer.Set( "b_attack", false );
		_renderer.Set( "holdtype", "none" );
		_renderer.Set( "move_speed", 0f );
		_renderer.Set( "move_groundspeed", 0f );
		_renderer.Set( "move_y", 0f );
		_renderer.Set( "move_x", 0f );
		_renderer.Set( "move_z", 0f );
		_renderer.Set( "b_grounded", false );
		_renderer.Set( "speed_move", 1f );
		_renderer.Set( "move_rotationspeed", 0f );
		_renderer.Set( "wish_speed", 0f );
		_renderer.Set( "wish_groundspeed", 0f );
		_renderer.Set( "wish_y", 0f );
		_renderer.Set( "wish_x", 0f );
		_renderer.Set( "wish_z", 0f );

		ResetAim();
	}
}
