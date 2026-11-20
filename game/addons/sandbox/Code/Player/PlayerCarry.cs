/// <summary>
/// Lets the player pick up light physics objects.
/// </summary>
public sealed class PlayerCarry : Component, Local.IPlayerEvents
{
	[RequireComponent] public Player Player { get; set; }

	/// <summary>
	/// When disabled, nobody can pick things up with use.
	/// </summary>
	[Title( "Carry Props" ), Group( "Gameplay" ), ServerSettings.Section( "Props" ), Order( 21 )]
	[ConVar( "sandbox.carry", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Players can pick up light props with use." )]
	public static bool AllowCarry { get; set; } = true;

	/// <summary>
	/// The heaviest object players can pick up
	/// </summary>
	[Range( 0, 1000 )]
	[Title( "Carry Max Mass" ), Group( "Gameplay" ), ServerSettings.Section( "Props" ), Order( 22 )]
	[ConVar( "sandbox.carry.maxmass", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "The heaviest object, in kg, players can pick up with use." )]
	public static float MaxMass { get; set; } = 80.0f;

	/// <summary>
	/// How fast prop will fly once you throw it out of hands
	/// </summary>
	[Property] public float ThrowSpeed { get; set; } = 500.0f;

	/// <summary>
	/// How far in front of the eyes the near side of a carried object is held
	/// </summary>
	[Property] public float HoldDistance { get; set; } = 40.0f;

	/// <summary>
	/// We let go once the object gets this far from where we're holding it, like when it's pushed against a wall
	/// </summary>
	[Property] public float BreakDistance { get; set; } = 96.0f;

	/// <summary>
	/// The object we're carrying
	/// </summary>
	[Sync] public GameObject Held { get; private set; }

	public Rigidbody HeldBody => Held?.GetComponent<Rigidbody>();

	public bool IsCarrying => Held.IsValid();

	public bool IsRotating { get; private set; }

	// Worked out from the holder's eye on every machine, a synced target would step at the network rate
	Transform _holdTarget;

	readonly PhysicsHold _hold = new();
	readonly HoldRotation _rotation = new();

	[Sync] Rotation _grabOffset { get; set; }
	TimeSince _timeSincePickup;

	/// <summary>
	/// The <see cref="_grabOffset"/> we're turning towards, at most <see cref="MaxAngular"/> degrees a second.
	/// </summary>
	Rotation _targetOffset;

	/// <summary>
	/// Scales how fast we can turn what we're holding. Starts at nothing on pickup and builds up over half a second.
	/// </summary>
	float _turnScale;

	const float MaxAngular = 360.0f * 10.0f;

	/// <summary>
	/// An axis of what we pick up within 30 degrees of one of ours lines up with it.
	/// </summary>
	static readonly float AlignCosine = MathF.Cos( MathX.DegreeToRadian( 30.0f ) );

	bool _rotateBlocked;
	TimeSince _timeSinceUsePressed;

	/// <summary>
	/// Releasing use within this long of pressing it drops what we're holding, holding it longer rotates.
	/// </summary>
	const float UseTapTime = 0.2f;

	bool _attackBlocked;

	/// <summary>
	/// What we had out before picking something up, to take it back out when we let go.
	/// </summary>
	BaseSandboxWeapon _weaponBeforeCarry;

	/// <summary>
	/// How long a client waits for the host to put away <see cref="_weaponBeforeCarry"/> before taking it as refused.
	/// </summary>
	const float HolsterTimeout = 1.0f;

	/// <summary>
	/// The object our joint is moving, so we can tell it when we let go.
	/// </summary>
	GameObject _simulated;
	bool _thrown;

	GameObject _refused;
	GameObject Claim => Networking.IsHost && IsProxy ? _simulated : Held;

	/// <summary>
	/// Simulating instance only, clamp the target the holder sends to this far from their eyes
	/// </summary>
	const float MaxReach = 256.0f;

	/// <summary>
	/// Called by the <see cref="Player"/> on their own machine every frame, before the weapon gets input.
	/// </summary>
	internal void OnControl()
	{
		if ( IsCarrying )
		{
			ControlHeld();
			return;
		}

		IsRotating = false;
		BlockAttack();

		if ( !Input.Pressed( "use" ) || Player.IsSeated ) return;

		if ( Player.Controller.Hovered.IsValid() || Player.Controller.Pressed.IsValid() ) return;

		if ( !TryPickup( FindTarget() ) ) return;

		Input.Clear( "use" );
	}

	void ControlHeld()
	{
		if ( !CanKeepCarrying() )
		{
			Drop();
			return;
		}

		var active = Player.GetComponent<PlayerInventory>()?.ActiveWeapon;
		if ( active.IsValid() && (active != _weaponBeforeCarry || _timeSincePickup > HolsterTimeout) )
		{
			_weaponBeforeCarry = null;
			Drop();
			return;
		}

		_rotation.UpdateSnapping();

		var useDown = Input.Down( "use" );
		if ( Input.Pressed( "use" ) ) _timeSinceUsePressed = 0;

		// The press that picked it up is still blocked, so letting go of it doesn't drop
		var useTapped = Input.Released( "use" ) && !_rotateBlocked && _timeSinceUsePressed < UseTapTime;
		if ( !useDown ) _rotateBlocked = false;
		Input.Clear( "use" );

		if ( useTapped )
		{
			Drop();
			return;
		}

		if ( Input.Pressed( "attack1" ) ) Throw();
		else if ( Input.Pressed( "attack2" ) ) Drop();

		_attackBlocked = true;
		BlockAttack();

		IsRotating = IsCarrying && useDown && !_rotateBlocked;
		if ( IsRotating )
		{
			var move = Input.AnalogMove;
			Player.Controller.WishVelocity = Vector3.Zero;
			_targetOffset = _rotation.Rotate( HoldFrame, move, true );
		}

		TurnToTarget();
	}

	/// <summary>
	/// What we carry turns with our yaw but not our pitch.
	/// </summary>
	Rotation HoldFrame => PhysicsHold.Stabilize( Player.EyeTransform.Rotation );

	void BlockAttack()
	{
		if ( !_attackBlocked ) return;

		if ( !Input.Down( "attack1" ) && !Input.Down( "attack2" ) && !IsCarrying )
			_attackBlocked = false;

		Input.Clear( "attack1" );
		Input.Clear( "attack2" );
		Input.SetLastAction( "attack1", false );
		Input.SetLastAction( "attack2", false );
	}

	bool CanKeepCarrying()
	{
		var body = HeldBody;
		if ( !body.IsValid() || !body.MotionEnabled || !body.PhysicsBody.IsValid() ) return false;
		if ( Player.IsSeated ) return false;

		if ( Player.Controller.GroundObject?.Root == Held.Root ) return false;

		if ( _timeSincePickup > 0.5f && body.PhysicsBody.MassCenter.Distance( _holdTarget.Position ) > BreakDistance ) return false;

		return true;
	}

	/// <summary>
	/// The body we're looking at within reach, if any.
	/// </summary>
	Rigidbody FindTarget()
	{
		var eye = Player.EyeTransform;
		var reach = Player.Controller.ReachLength;

		// Widen the ray a little if it misses, so small things aren't fiddly to grab
		for ( var radius = 0.0f; radius <= 4.0f; radius += 2.0f )
		{
			var tr = Scene.Trace.Ray( eye.Position, eye.Position + eye.Forward * (reach - radius) )
				.Radius( radius )
				.IgnoreGameObjectHierarchy( GameObject.Root )
				.Run();

			if ( !tr.Hit ) continue;
			return tr.Body is not null ? tr.Component as Rigidbody : null;
		}

		return null;
	}

	/// <summary>
	/// Whether we're allowed to pick up <paramref name="body"/>. Asks the object and any listeners through a <see cref="PlayerCarryEvent"/>.
	/// </summary>
	public bool CanCarry( Rigidbody body )
	{
		if ( !AllowCarry ) return false;
		if ( !body.IsValid() || !body.MotionEnabled || !body.PhysicsBody.IsValid() ) return false;

		var go = body.GameObject;
		if ( go.Root == GameObject.Root ) return false;
		if ( go.Root.Tags.Has( "player" ) || go.Root.Tags.Has( "npc" ) ) return false;

		// Someone else has it, two joints would fight over it
		if ( Scene.GetAll<PlayerCarry>().Any( x => x != this && x.Claim?.Root == go.Root ) ) return false;

		var bodies = new HashSet<Rigidbody>();
		PhysicsHold.GetConnectedBodies( go, bodies );
		if ( bodies.Any( x => !x.MotionEnabled ) ) return false;

		var e = new PlayerCarryEvent
		{
			Player = Player,
			Target = go,
			Body = body,
			Mass = bodies.Sum( x => x.Mass ),
			MaxMass = MaxMass
		};

		go.Root.RunEvent<ICarryEvent>( x => x.OnCarryGrab( e ) );
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, x => x.OnCarry( e ) );
		Global.IPlayerEvents.Post( x => x.OnPlayerCarry( e ) );

		return !e.Cancelled && e.Mass <= e.MaxMass;
	}

	/// <summary>
	/// Picks up <paramref name="body"/> if we're allowed to (owner only)
	/// </summary>
	public bool TryPickup( Rigidbody body )
	{
		if ( IsProxy || IsCarrying ) return false;
		if ( !CanCarry( body ) ) return false;
		if ( !Holster() ) return false;

		Held = body.GameObject;

		_grabOffset = HoldFrame.Inverse * body.WorldRotation;
		_targetOffset = AlignRotation( _grabOffset, AlignCosine );
		_turnScale = 0;
		_rotation.Reset( _targetOffset );
		_holdTarget = GetHoldTarget();
		_timeSincePickup = 0;
		_rotateBlocked = true;

		return true;
	}

	/// <summary>
	/// Lines up any axis of <paramref name="rotation"/> within acos( <paramref name="cosine"/> ) of a frame axis with it,
	/// up first, so things are picked up straight.
	/// </summary>
	static Rotation AlignRotation( Rotation rotation, float cosine )
	{
		Vector3[] axes = [rotation.Forward, rotation.Left, rotation.Up];

		// Up goes first, so a lined up top wins over the sides
		for ( var axis = 2; axis >= 0; axis-- )
		{
			var direction = axes[axis];
			for ( var component = 0; component < 3; component++ )
			{
				if ( MathF.Abs( direction[component] ) <= cosine ) continue;

				var snapped = Vector3.Zero;
				snapped[component] = direction[component] < 0 ? -1 : 1;
				axes[axis] = snapped;
				SquareUp( axes, axis );
				break;
			}
		}

		return Rotation.LookAt( axes[0], axes[2] );
	}

	/// <summary>
	/// Rebuilds the other two axes square to <paramref name="fixedAxis"/>, keeping them as close to where they were as we can.
	/// </summary>
	static void SquareUp( Vector3[] axes, int fixedAxis )
	{
		var next = (fixedAxis + 1) % 3;
		var last = (fixedAxis + 2) % 3;

		axes[last] = Vector3.Cross( axes[fixedAxis], axes[next] ).Normal;
		axes[next] = Vector3.Cross( axes[last], axes[fixedAxis] ).Normal;
	}

	/// <summary>
	/// Turns what we're holding towards <see cref="_targetOffset"/>, slowly at first after pickup.
	/// </summary>
	void TurnToTarget()
	{
		_turnScale = _turnScale.Approach( 1.0f, Time.Delta * 2.0f );

		var maxAngular = MaxAngular * _turnScale * _turnScale * _turnScale;
		var distance = _grabOffset.Distance( _targetOffset );
		if ( distance <= 0.0f ) return;

		var step = maxAngular * Time.Delta;
		_grabOffset = step >= distance ? _targetOffset : Rotation.Slerp( _grabOffset, _targetOffset, step / distance );
	}

	/// <summary>
	/// Lets go of what we're carrying where it is (owner only)
	/// </summary>
	public void Drop()
	{
		if ( IsProxy || !IsCarrying ) return;

		Held = null;
		IsRotating = false;
		_attackBlocked = true;

		Unholster();
	}

	/// <summary>
	/// Puts away the active weapon, carrying takes both hands. False if it couldn't be put away.
	/// </summary>
	bool Holster()
	{
		var inventory = Player.GetComponent<PlayerInventory>();
		if ( !inventory.IsValid() || !inventory.ActiveWeapon.IsValid() ) return true;

		var weapon = inventory.ActiveWeapon;
		inventory.SwitchWeapon( null, true );

		if ( Networking.IsHost && inventory.ActiveWeapon.IsValid() ) return false;

		_weaponBeforeCarry = weapon;
		return true;
	}

	/// <summary>
	/// Takes back out the weapon we put away to pick something up, unless something else has come out since.
	/// </summary>
	void Unholster()
	{
		var weapon = _weaponBeforeCarry;
		_weaponBeforeCarry = null;

		var inventory = Player.GetComponent<PlayerInventory>();
		if ( !inventory.IsValid() || inventory.ActiveWeapon.IsValid() ) return;
		if ( !weapon.IsValid() || weapon.Owner != Player || Player.IsSeated ) return;

		inventory.SwitchWeapon( weapon );
	}

	/// <summary>
	/// Throws what we're carrying where we're looking (owner only)
	/// </summary>
	public void Throw()
	{
		if ( IsProxy || !IsCarrying ) return;

		ThrowBody( HeldBody, Player.EyeTransform.Forward * ThrowSpeed );
		Drop();
	}

	[Rpc.Host]
	void ThrowBody( Rigidbody body, Vector3 velocity )
	{
		// Only what we accepted and are moving for them, not a pickup we refused or haven't got to yet
		if ( !body.IsValid() || body.IsProxy || _simulated != body.GameObject ) return;

		_thrown = true;
		ReleaseHold();
		DamageBlame.Blame( body.GameObject, Player );

		body.ApplyImpulse( velocity * body.Mass );
	}

	Transform GetHoldTarget()
	{
		var eye = Player.EyeTransform;
		var body = HeldBody;
		var center = body.PhysicsBody.MassCenter;

		// Hold it no further up or down than 75 degrees, so looking straight down doesn't put it under our feet
		// Built from the stable yaw, reading the angles back off the eye flips the yaw around when looking straight down
		var pitch = MathX.RadianToDegree( -MathF.Asin( eye.Forward.z.Clamp( -1.0f, 1.0f ) ) );
		eye = eye.WithRotation( HoldFrame * Rotation.FromPitch( pitch.Clamp( -75.0f, 75.0f ) ) );

		var distance = PhysicsHold.ClampGrabDistance( body, center, eye, 0.0f, HoldDistance );
		var position = eye.Position + eye.Forward * distance;

		// And keep it our radius plus its own from the line through the middle of us, so at no angle can it push us away
		var forward = eye.Forward;
		var extent = MathF.Abs( Vector3.Dot( center - body.FindClosestPoint( center - forward * 4096.0f ), forward ) );
		var radius = Player.Controller.BodyRadius + extent;

		var bottom = Player.WorldPosition;
		var nearest = new Line( bottom, bottom + Vector3.Up * Player.Controller.CurrentHeight ).ClosestPoint( position );
		var delta = position - nearest;
		if ( delta.Length < radius && !delta.IsNearZeroLength )
			position = nearest + delta.Normal * radius;

		return new Transform( position, HoldFrame * _grabOffset );
	}

	protected override void OnFixedUpdate()
	{
		var body = HeldBody;

		if ( body.IsValid() && body.PhysicsBody.IsValid() )
			_holdTarget = GetHoldTarget();

		// switched straight to another object, the joint still holds the old one
		if ( _simulated.IsValid() && _simulated != Held )
			ReleaseHold();

		if ( !CanSimulate( body ) )
		{
			ReleaseHold();
			_thrown = false;
			_refused = null;
			return;
		}

		if ( _thrown ) return;

		if ( !_hold.IsHolding )
		{
			// The holder asked for this on their own machine, check it again before we move it for them
			if ( _refused == Held ) return;
			if ( IsProxy && (!CanCarry( body ) || !IsInReach( body )) )
			{
				_refused = Held;
				RefuseCarry( Held );
				return;
			}

			// Critically damped so it stays at the hold point when we turn, rather than trailing behind
			_hold.Hold( body, body.PhysicsBody.MassCenter, 1.0f );
			_simulated = Held;
			_simulated.Root.RunEvent<ICarryEvent>( x => x.OnCarryPickup( Player ) );
		}

		_hold.Move( IsProxy ? ClampToReach( _holdTarget ) : _holdTarget );
		DamageBlame.Blame( _simulated, Player );
	}

	/// <summary>
	/// Whether the holder can see <paramref name="body"/> within use reach, with some slack for where we see their eyes.
	/// </summary>
	bool IsInReach( Rigidbody body )
	{
		var eye = Player.EyeTransform.Position;
		var closest = body.FindClosestPoint( eye );
		if ( closest.Distance( eye ) > Player.Controller.ReachLength + 32.0f ) return false;

		var tr = Scene.Trace.Ray( eye, closest )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		return !tr.Hit || tr.GameObject?.Root == body.GameObject.Root;
	}

	[Rpc.Owner]
	void RefuseCarry( GameObject held )
	{
		// They may have let go and picked up something else since
		if ( Held == held ) Drop();
	}

	Transform ClampToReach( Transform target )
	{
		var eye = Player.EyeTransform.Position;
		return target.WithPosition( eye + (target.Position - eye).ClampLength( MaxReach ) );
	}

	static bool CanSimulate( Rigidbody body )
	{
		if ( !body.IsValid() ) return false;

		// Only move the body if we own it, and it's dynamic.
		if ( body.IsProxy ) return false;
		if ( !body.MotionEnabled ) return false;
		if ( !body.PhysicsBody.IsValid() ) return false;

		return true;
	}

	void ReleaseHold()
	{
		_hold.Release();

		var held = _simulated;
		_simulated = null;

		if ( !held.IsValid() ) return;

		held.Root.RunEvent<ICarryEvent>( x => x.OnCarryDrop( Player, _thrown ) );
	}

	protected override void OnDisabled()
	{
		ReleaseHold();

		if ( !IsProxy ) Held = null;

		IsRotating = false;
		_thrown = false;
		_attackBlocked = false;
		_weaponBeforeCarry = null;
	}

	void Local.IPlayerEvents.OnMove( ref bool allowMovement )
	{
		if ( IsRotating ) allowMovement = false;
	}

	void Local.IPlayerEvents.OnCameraMove( ref Angles angles )
	{
		if ( IsRotating ) angles = default;
	}
}
