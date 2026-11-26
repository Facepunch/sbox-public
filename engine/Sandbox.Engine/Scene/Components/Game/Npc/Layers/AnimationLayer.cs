using Sandbox.Engine.Resources;
namespace Sandbox.Npcs.Layers;

/// <summary>
/// Animation state for the NPC's model: movement, look target and one-shot triggers.
/// Synced properties replicate it to all clients, where an animator component (such as
/// <see cref="CitizenNpcAnimator"/>) reads it and drives the model. Also turns the body
/// toward the look target and plays footsteps from the model's footstep events.
/// A mover publishes its state here on the host: <see cref="SetMove"/>, <see cref="IsGrounded"/>,
/// <see cref="Traversal"/> and <see cref="TraversalDirection"/>. One that owns the NPC's orientation, such as a
/// wall-walker, turns <see cref="TurnsBody"/> off.
/// </summary>
public partial class AnimationLayer : BaseNpcLayer
{
	/// <summary>
	/// How an NPC holds its weapon, for its animator.
	/// </summary>
	[Expose]
	public enum WeaponStances
	{
		/// <summary>Up and ready to fire.</summary>
		Ready,

		/// <summary>Lowered, at ease.</summary>
		Lowered,
	}

	// Degrees per second, constant: an exponential turn reads as a jolting spin
	private const float BodyTurnSpeed = 120f;

	/// <summary>
	/// Whether this layer turns the standing NPC's body toward its look target. On by default. Turn it off when
	/// the mover owns the NPC's orientation; the head and eyes still follow the look target.
	/// </summary>
	[Property]
	public bool TurnsBody { get; set; } = true;

	private const float MaxHeadAngle = 45f;

	/// <summary>Play footstep sounds from the model's footstep animation events.</summary>
	[Property]
	public bool EnableFootstepSounds { get; set; } = true;

	/// <summary>Overall footstep volume multiplier.</summary>
	[Property]
	public float FootstepVolume { get; set; } = 1f;

	private TimeSince _timeSinceStep;

	// The persistent look target, host only. The position tracks the object's eyes while there is one.
	private Vector3? LookTarget { get; set; }
	private GameObject LookTargetObject { get; set; }

	// A temporary look target that overrides the persistent one until it expires.
	// Speech uses this to look whoever we're talking to
	// in the eyes for the duration of the line.
	private GameObject _addedLookTarget;
	private TimeUntil _addedLookExpires;

	private SkinnedModelRenderer _renderer => Npc.IsValid() ? Npc.Renderer : null;
	private bool _turningToTarget;

	/// <summary>
	/// How fast and which way the mover is taking the NPC, set through <see cref="SetMove"/> and synced to all clients.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public Vector3 MoveVelocity { get; private set; }
	/// <summary>
	/// Whether the NPC is on its feet rather than in the air, for the animator. Set on the host and synced. The NPC
	/// sets it every update for a mover that uses the ground (<see cref="MovementLayer.UsesGround"/>); any other
	/// mover sets it itself.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public bool IsGrounded { get; set; } = true;

	/// <summary>
	/// The type of the nav link currently being animated, such as <see cref="NavMeshLinkType.Ladder"/>. Null off links and
	/// on plain ones, and while approaching a drop. Set on the host by the mover and synced.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public NavMeshLinkType Traversal { get; set; }

	/// <summary>
	/// Which way the link being crossed goes: 1 up, -1 down, 0 level. Set on the host by the mover and synced.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public int TraversalDirection { get; set; }

	/// <summary>
	/// Whether the NPC has a look target right now, synced to all clients.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public bool IsLooking { get; private set; }

	/// <summary>
	/// Whether the weapon is held up and ready or lowered, at ease. Synced to all clients.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public WeaponStances WeaponStance { get; set; }

	/// <summary>
	/// The object the NPC is looking at, when its look target is an object. Synced so each client resolves the eyes
	/// itself: the player being looked at sees the NPC look at their camera, everyone else at their avatar's eyes.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public GameObject LookObject { get; private set; }

	// The looked at position when there's no LookObject.
	[Sync( SyncFlags.FromHost ), Expose] private Vector3 LookPoint { get; set; }

	/// <summary>
	/// Where the NPC is looking, while <see cref="IsLooking"/>. Resolved from <see cref="LookObject"/> on each client when there is one.
	/// </summary>
	public Vector3 CalculateLookPosition() => LookObject.IsValid() ? BaseNpc.CalculateEyePosition( LookObject ) : LookPoint;

	/// <summary>
	/// Fired on every client by <see cref="Trigger"/>.
	/// </summary>
	public event Action<string> Triggered;

	protected override void OnEnabled()
	{
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

	private void OnFootstepEvent( SceneModel.FootstepEvent e )
	{
		if ( !EnableFootstepSounds ) return;
		if ( _timeSinceStep < 0.2f ) return;

		var volume = e.Volume * MoveVelocity.Length.Remap( 0, 400, 0, 1 );
		if ( volume <= 0.1f ) return;

		_timeSinceStep = 0;
		PlayFootstepSound( e.Transform.Position, volume, e.FootId );
	}

	private void PlayFootstepSound( Vector3 position, float volume, int foot )
	{
		// Traced here: footsteps play on every client, and only the host knows the NPC's ground
		var tr = Scene.Trace.Ray( position + Vector3.Up * 16f, position - Vector3.Up * 16f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithCollisionRules( Tags )
			.Run();

		var surface = tr.Hit ? tr.Surface : null;
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
		if ( !Networking.IsHost || !Npc.IsValid() )
			return;

		LookObject = _addedLookTarget.IsValid() && !_addedLookExpires
			? _addedLookTarget
			: LookTargetObject;

		if ( LookTargetObject.IsValid() )
			LookTarget = BaseNpc.CalculateEyePosition( LookTargetObject );

		var lookPos = LookObject.IsValid() ? (Vector3?)BaseNpc.CalculateEyePosition( LookObject ) : LookTarget;

		// Held or thrown - let go of whatever we were looking at, or the head and upper body keep
		// twisting back toward it while the body gets turned around
		if ( Npc.IsAirborne || !NpcConVars.Enabled )
		{
			LookObject = null;
			lookPos = null;
		}

		IsLooking = lookPos.HasValue;
		if ( lookPos.HasValue )
		{
			if ( !LookObject.IsValid() )
				LookPoint = lookPos.Value;

			TurnBodyToward( lookPos.Value );
		}
	}

	/// <summary>
	/// Look at a target for a limited time, overriding the persistent look target.
	/// The NPC aims at the target's eyes. Call again to extend - when it expires we
	/// fall back to the persistent target, if any.
	/// </summary>
	internal void AddLookTarget( GameObject target, float duration )
	{
		_addedLookTarget = target;
		_addedLookExpires = duration;
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
	}

	/// <summary>
	/// Returns true if the NPC body is facing the current look target within MaxHeadAngle.
	/// </summary>
	internal bool IsFacingTarget()
	{
		if ( !LookTarget.HasValue ) return true;
		if ( _renderer is null ) return true;

		var direction = (LookTarget.Value.WithZ( 0 ) - Npc.WorldPosition.WithZ( 0 )).Normal;
		var angleToTarget = Vector3.GetAngle( Npc.BodyRotation.Forward.WithZ( 0 ), direction );
		return angleToTarget <= MaxHeadAngle;
	}

	private void TurnBodyToward( Vector3 targetPosition )
	{
		if ( !TurnsBody || _renderer is null ) return;

		var flatDirection = (targetPosition - Npc.WorldPosition).WithZ( 0 ).Normal;

		// While travelling, NavigationLayer faces the body along the movement direction, so the
		// look-at just tracks with the head/eyes -- turning the body too would make it run
		// sideways. Standing still the body is ours to turn.
		if ( Npc.Movement.IsValid() && Npc.Movement.IsMoving )
			return;

		var current = Npc.BodyRotation;
		var angleToTarget = Vector3.GetAngle( current.Forward, flatDirection );

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
			var remaining = current.Distance( targetRotation );

			if ( remaining > 0.1f )
			{
				var step = MathF.Min( 1f, BodyTurnSpeed * Time.Delta / remaining );
				Npc.SetBodyRotation( Rotation.Slerp( current, targetRotation, step ) );
			}
		}
	}

	/// <summary>
	/// How far the NPC is crouched down, from 0 standing to 1, such as while it gets up off the ground. Set on every
	/// client, for animators.
	/// </summary>
	public float Crouch { get; internal set; }

	/// <summary>
	/// Records move state for replication. Called on the host by whatever moves the NPC.
	/// </summary>
	public void SetMove( Vector3 velocity )
	{
		MoveVelocity = velocity;
	}

	/// <summary>
	/// Held and carried through physics, such as by a physgun. Synced to all clients, for animators.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public bool IsCarried { get; internal set; }

	/// <summary>
	/// Fire a named one-shot (such as "attack") on every client, for animators to react to through <see cref="Triggered"/>.
	/// </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void Trigger( string name )
	{
		Triggered?.Invoke( name );
	}
}
