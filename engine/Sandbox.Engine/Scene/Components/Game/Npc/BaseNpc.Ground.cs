using Sandbox.Npcs.Layers;

namespace Sandbox.Npcs;

public partial class BaseNpc
{
	private static readonly string[] GroundExcludedTags = ["npc"];

	/// <summary>
	/// Are we on the ground?
	/// </summary>
	public bool IsOnGround => GroundObject.IsValid();

	/// <summary>
	/// The object we're standing on. Null if we're standing on nothing.
	/// </summary>
	public GameObject GroundObject { get; private set; }

	/// <summary>
	/// The collider or rigidbody we're standing on. Null if we're standing on nothing.
	/// </summary>
	internal Component GroundComponent { get; private set; }

	/// <summary>
	/// If we're standing on a surface this is it
	/// </summary>
	public Surface GroundSurface { get; private set; }

	/// <summary>
	/// The entity carrying this NPC through physics, such as something that grabbed it. See <see cref="StartCarry"/>.
	/// </summary>
	public GameObject PhysicsCarrier { get; private set; }

	/// <summary>
	/// Something takes hold of this NPC and carries it through physics, such as a tool that grabbed it. The NPC is
	/// off its feet and its mover lets go until <see cref="StopCarry"/>. A new carrier takes it from the last one.
	/// </summary>
	public void StartCarry( GameObject carrier )
	{
		if ( carrier.IsValid() )
			SetCarrier( carrier );
	}

	/// <summary>
	/// The carrier lets go of this NPC. Does nothing when something else carries it by now.
	/// </summary>
	public void StopCarry( GameObject carrier )
	{
		if ( PhysicsCarrier == carrier )
			SetCarrier( null );
	}

	void SetCarrier( GameObject carrier )
	{
		var previous = PhysicsCarrier;
		if ( previous == carrier )
			return;

		PhysicsCarrier = carrier;

		// Taken hold of getting up, it struggles instead
		if ( carrier.IsValid() )
			EndGetUp();

		// A mover that doesn't use the ground gives its body to physics only while it is carried.
		if ( Networking.IsHost && Movement.IsValid() && KnockdownState != KnockdownStates.Down )
			SetPhysicsControl( carrier.IsValid() || (UsesGround && !IsOnGround) );

		if ( Networking.IsHost ) UpdateBodyOwner();

		// A flick is told from where it was when taken hold of
		_swingVelocity = _rigidbody.IsValid() ? _rigidbody.Velocity : default;
		_sinceSwingSample = 0;

		if ( previous.IsValid() )
			OnCarryStopped( previous );

		if ( carrier.IsValid() )
			OnCarryStarted( carrier );
	}

	/// <summary>
	/// Something took hold of this NPC, see <see cref="StartCarry"/>.
	/// </summary>
	protected virtual void OnCarryStarted( GameObject carrier ) { }

	/// <summary>
	/// The carrier let go of this NPC, see <see cref="StopCarry"/>. The base keeps whichever way it was turned,
	/// rather than snapping back to whatever it was looking at or doing before.
	/// </summary>
	protected virtual void OnCarryStopped( GameObject carrier )
	{
		Animation.ClearLookTarget();
		EndCurrentSchedule();
	}

	/// <summary>
	/// Whether our mover keeps us on the ground, so the ground probe, landing and impact damage apply.
	/// </summary>
	bool UsesGround => Movement.IsValid() && Movement.UsesGround;

	GameObject _airContact;
	TimeSince _sinceAirMovement;

	void CategorizeGround()
	{
		if ( !UsesGround )
			return;

		if ( IsOnGround || PhysicsCarrier.IsValid() || !_rigidbody.IsValid() || _rigidbody.Velocity.Length > 5 )
			_sinceAirMovement = 0;

		if ( PhysicsCarrier.IsValid() )
		{
			// Never grounded while carried, even if we are on the ground
			UpdateGroundFromTraceResult( default );
			return;
		}

		// The navigation layer carries us across a link, off the ground or not
		if ( Movement.IsValid() && Movement.IsCrossingLink )
			return;

		// Just launched: stay off the floor we left until we stop rising, or the ground probe lands us at once
		if ( _launched && (_sinceLaunch < 0.1f || (_rigidbody.IsValid() && _rigidbody.Velocity.z > 0f)) )
		{
			UpdateGroundFromTraceResult( default );
			return;
		}

		var up = WorldRotation.Up;
		var from = WorldPosition + up * 16;
		// The mover eases us down steps, so while it walks us we can be up to a step above the floor
		var to = WorldPosition - up * (IsOnGround ? 8 + Movement.StepHeight : 8);

		var tr = GroundTrace( Scene.Trace.Ray( from, to ) ).Run();

		// Started inside something, such as a prop we are pushing: it says nothing about the ground, so look past it
		if ( tr.StartedSolid && tr.GameObject.IsValid() )
			tr = GroundTrace( Scene.Trace.Ray( from, to ) ).IgnoreGameObjectHierarchy( tr.GameObject.Root ).Run();

		if ( !tr.StartedSolid && tr.Hit )
		{
			UpdateGroundFromTraceResult( tr );
		}
		else if ( RestingOn() is { Hit: true } support )
		{
			// The ray from our middle misses, but the body stands on something, like a ledge's edge
			UpdateGroundFromTraceResult( support );
		}
		else if ( _sinceAirMovement > 0.5f && _airContact.IsValid() )
		{
			// Resting somewhere the ray misses, like a ledge corner: land, so the agent can step us down
			GroundObject = _airContact;
			GroundComponent = null;
			GroundSurface = null;
			OnGroundedChanged();
		}
		else
		{
			UpdateGroundFromTraceResult( default );
		}
	}

	// The body itself swept down a little, like PlayerController's hull probe, while it isn't rising or falling
	SceneTraceResult RestingOn()
	{
		if ( !_rigidbody.IsValid() || MathF.Abs( _rigidbody.Velocity.z ) > 5f || _rigidbody.PhysicsBody is not { IsValid: true } body ) return default;

		// From a little above: resting on it, the body already touches it
		var up = WorldRotation.Up;
		var tr = GroundTrace( Scene.Trace.Body( body, body.Transform.WithPosition( body.Position + up * 2 ), body.Position - up * 2 ) ).Run();

		return !tr.StartedSolid && tr.Hit && tr.Normal.z > 0.7f ? tr : default;
	}

	private SceneTrace GroundTrace( SceneTrace trace ) => trace.IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).WithoutTags( GroundExcludedTags );

	void UpdateGroundFromTraceResult( SceneTraceResult tr )
	{
		bool wasGrounded = IsOnGround;

		var body = tr.Body;
		GroundObject = body?.GameObject;
		GroundComponent = body?.Component;
		GroundSurface = tr.Surface;

		if ( IsOnGround != wasGrounded )
		{
			OnGroundedChanged();
		}
	}

	private void OnGroundedChanged()
	{
		_airContact = null;

		UpdateBodyOwner();
		if ( UsesGround ) SetPhysicsControl( !IsOnGround );
	}

	// Another mover has the body. What we stood on no longer counts: a ground mover starts in the air until the
	// probe finds the floor, and any other mover holds the body itself.
	void OnMovementChanged()
	{
		GroundObject = null;
		GroundComponent = null;
		GroundSurface = null;
		_airContact = null;
		_launched = false;

		UpdateBodyOwner();
		SetPhysicsControl( PhysicsCarrier.IsValid() || UsesGround );
	}

	// The one place that decides whether physics moves the body or the mover places it
	internal void UpdateBodyOwner()
	{
		if ( !_rigidbody.IsValid() ) return;

		_rigidbody.MotionEnabled = KnockdownState == KnockdownStates.Down || PhysicsCarrier.IsValid() || _launched
			|| (UsesGround && !IsOnGround) || (Movement.IsValid() && Movement.SimulatesBody);
	}

	void SetPhysicsControl( bool physics )
	{
		if ( physics )
			Movement.OnPhysicsControlStarted();
		else
			Movement.OnPhysicsControlEnded();
	}
}
