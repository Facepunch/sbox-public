namespace Sandbox.Npcs.Layers;

/// <summary>
/// Moves the NPC: <see cref="NavigationLayer"/> walks the navmesh, other movers fly, swim or roll.
/// Tasks and schedules command it through <see cref="MoveTo"/>, <see cref="Stop"/> and <see cref="Status"/>.
/// </summary>
public abstract class MovementLayer : BaseNpcLayer
{
	/// <summary>
	/// Where a <see cref="MovementLayer"/> is with the move it was asked for.
	/// </summary>
	[Expose]
	public enum MovementStatus
	{
		/// <summary>No move was asked for, or it was stopped.</summary>
		Idle,

		/// <summary>On its way to the move target.</summary>
		Moving,

		/// <summary>Within the stop distance of the move target.</summary>
		Arrived,

		/// <summary>It cannot get to the move target.</summary>
		Failed,
	}

	/// <summary>
	/// Where the current move is going, or null when no move was asked for.
	/// </summary>
	public Vector3? MoveTarget { get; protected set; }

	/// <summary>
	/// How close the current move has to get to <see cref="MoveTarget"/> to count as arrived.
	/// </summary>
	public float StopDistance { get; protected set; } = 10f;

	/// <summary>
	/// The NPC's normal walking speed.
	/// </summary>
	[Property, Title( "Walk Speed" ), Range( 20, 400f ), Step( 1 ), ClientEditable, Sync( SyncFlags.FromHost )]
	public float WalkSpeed { get; set; } = 100f;

	/// <summary>
	/// Speed used to run when the destination is far away -- HL-style: walk when close, run
	/// when there's ground to cover. Acts as a floor, so a higher <see cref="WalkSpeed"/> is never slowed down.
	/// </summary>
	[Property, Title( "Run Speed" ), Range( 20, 400f ), Step( 1 ), ClientEditable, Sync( SyncFlags.FromHost )]
	public float RunSpeed { get; set; } = 190f;

	/// <summary>How quickly the body turns to face the movement direction.</summary>
	[Property]
	public float TurnSpeed { get; set; } = 4f;

	/// <summary>
	/// True while the NPC is actually travelling. Standing NPCs are free to turn
	/// their body toward whatever they're looking at.
	/// </summary>
	public abstract bool IsMoving { get; }

	/// <summary>
	/// Command this layer to move to a target
	/// </summary>
	public abstract void MoveTo( Vector3 target, float stopDistance = 10f, float? speed = null, GameObject faceTarget = null );

	/// <summary>
	/// Stop moving and forget the current move target.
	/// </summary>
	public abstract void Stop();

	/// <summary>
	/// Where the mover is with the move it was asked for. Unlike <see cref="IsMoving"/> it is about the request,
	/// not about whether the body is in motion this moment.
	/// </summary>
	public abstract MovementStatus Status { get; }

	/// <summary>
	/// Project <paramref name="point"/> onto this mover's movement space, such as the nearest spot on the navmesh.
	/// Null when no suitable point is found. This does not check whether a route to it exists.
	/// </summary>
	public virtual Vector3? ProjectPoint( Vector3 point ) => point;

	/// <summary>
	/// Whether this mover keeps the NPC on the ground, so the NPC runs its ground probe, landing and impact damage.
	/// </summary>
	protected internal virtual bool UsesGround => false;

	/// <summary>
	/// How far above the floor this mover can carry a grounded NPC, such as easing it down a step.
	/// </summary>
	protected internal virtual float StepHeight => 0f;

	/// <summary>
	/// The body was handed to physics: launched, carried or fallen. This mover lets go of it until
	/// <see cref="OnPhysicsControlEnded"/>.
	/// </summary>
	protected internal virtual void OnPhysicsControlStarted() { }

	/// <summary>
	/// Physics handed the body back to this mover.
	/// </summary>
	protected internal virtual void OnPhysicsControlEnded() { }
}
