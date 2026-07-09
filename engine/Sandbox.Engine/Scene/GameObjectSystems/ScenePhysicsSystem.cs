using Sandbox.Utility;

namespace Sandbox;

/// <summary>
/// Ticks the physics in FrameStage.PhysicsStep
/// </summary>
[Expose]
sealed class ScenePhysicsSystem : GameObjectSystem<ScenePhysicsSystem>
{
	private PhysicsWorld PhysicsWorld;
	private HashSetEx<Collider> KeyframeColliders { get; set; } = new();
	private HashSet<Rigidbody> RigidBodies { get; set; } = new();
	private List<ISceneCollisionEvents> CollisionEvents { get; } = new();

	// Rigidbodies whose physics body moved this step (populated from PhysicsWorld.OnBodyActive).
	// We drain this each step instead of scanning every Rigidbody, so settled/asleep bodies cost nothing.
	private readonly HashSet<Rigidbody> MovedRigidbodies = new();
	private readonly List<Rigidbody> MovedBuffer = new();

	internal bool Enabled { get; set; }

	public ScenePhysicsSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.PhysicsStep, 0, UpdatePhysics, "UpdatePhysics" );
		Listen( Stage.FinishUpdate, 0, DebugDrawPhysics, "DebugDrawPhysics" );
	}

	/// <summary>
	/// Called by the scene when it creates its physics world. The world is created on
	/// demand by the first thing that needs physics - we shouldn't be the ones forcing
	/// it to exist.
	/// </summary>
	internal void OnPhysicsWorldCreated( PhysicsWorld world )
	{
		PhysicsWorld = world;
		PhysicsWorld.OnIntersectionStart += OnIntersectionStart;
		PhysicsWorld.OnIntersectionHit += OnIntersectionHit;
		PhysicsWorld.OnIntersectionUpdate += OnIntersectionUpdate;
		PhysicsWorld.OnIntersectionEnd += OnIntersectionEnd;
		PhysicsWorld.OnBodyOutOfBounds += OnBodyOutOfBounds;
		PhysicsWorld.OnBodyFellAsleep += OnBodyFellAsleep;
		PhysicsWorld.OnBodyActive += OnBodyActive;
	}

	public override void Dispose()
	{
		base.Dispose();

		if ( !PhysicsWorld.IsValid() )
			return;

		PhysicsWorld.OnIntersectionStart -= OnIntersectionStart;
		PhysicsWorld.OnIntersectionHit -= OnIntersectionHit;
		PhysicsWorld.OnIntersectionUpdate -= OnIntersectionUpdate;
		PhysicsWorld.OnIntersectionEnd -= OnIntersectionEnd;
		PhysicsWorld.OnBodyOutOfBounds -= OnBodyOutOfBounds;
		PhysicsWorld.OnBodyFellAsleep -= OnBodyFellAsleep;
		PhysicsWorld.OnBodyActive -= OnBodyActive;
	}

	void UpdatePhysics()
	{
		if ( Scene.IsEditor && !Enabled )
			return;

		using var _ = PerformanceStats.Timings.Physics.Scope();

		var idealHz = 120.0f;
		var idealStep = 1.0f / idealHz;
		int steps = (Time.Delta / idealStep).FloorToInt().Clamp( 1, 10 );

		//
		// Get collision events
		//
		CollisionEvents.Clear();
		Scene.GetAll( CollisionEvents );

		//
		// Called before UpdateKeyframeTransform on purpose, because I assume people are going to use this to move
		// their keyframes, and I want to catch those changes before the physics step.
		//
		IScenePhysicsEvents.Post( x => x.PrePhysicsStep() );

		//
		// Tell all the keyframe colliders to "move" their keyframes to their new position
		// - we do this right before the physics step
		// - this means that changes in FixedUpdate are immediate
		//
		// Box3D doesn't like us threading things
		// System.Threading.Tasks.Parallel.ForEach( KeyframeColliders.EnumerateLocked( true ), c => c.UpdateKeyframeTransform() );
		foreach ( var c in KeyframeColliders.EnumerateLocked( true ) )
		{
			c.UpdateKeyframeTransform();
		}

		// The actual physics step - if the world was never created there's nothing to step
		PhysicsWorld?.Step( Time.NowDouble, Time.Delta, steps );

		//
		// Update the positions of the rigidbodies that the solver reported as moved this step.
		// Asleep/settled bodies don't fire OnBodyActive, so they're skipped entirely.
		// We buffer then clear so UpdateTransformFromBody's transform-changed callbacks can't mutate the set mid-iteration.
		//
		MovedBuffer.Clear();
		foreach ( var rb in MovedRigidbodies )
		{
			if ( rb.IsValid() )
				MovedBuffer.Add( rb );
		}
		MovedRigidbodies.Clear();

		foreach ( var rb in MovedBuffer )
		{
			rb.UpdateTransformFromBody();
		}

		//
		// Called after the positions of everything are updated, because I assume that people are going to want
		// to access those positions and do shit with them.
		//
		IScenePhysicsEvents.Post( x => x.PostPhysicsStep() );
	}

	void OnIntersectionStart( PhysicsIntersection o )
	{
		if ( CollisionEvents is null ) return;

		var c = new Collision( new CollisionSource( o.Self ), new CollisionSource( o.Other ), o.Contact );
		foreach ( var e in CollisionEvents )
		{
			e.OnCollisionStart( c );
		}
	}

	void OnIntersectionHit( PhysicsIntersection o )
	{
		if ( CollisionEvents is null ) return;

		var c = new Collision( new CollisionSource( o.Self ), new CollisionSource( o.Other ), o.Contact );
		foreach ( var e in CollisionEvents )
		{
			e.OnCollisionHit( c );
		}
	}

	void OnIntersectionUpdate( PhysicsIntersection o )
	{
		if ( CollisionEvents is null ) return;

		var c = new Collision( new CollisionSource( o.Self ), new CollisionSource( o.Other ), o.Contact );
		foreach ( var e in CollisionEvents )
		{
			e.OnCollisionUpdate( c );
		}
	}

	void OnIntersectionEnd( PhysicsIntersectionEnd o )
	{
		if ( CollisionEvents is null ) return;

		var c = new CollisionStop( new CollisionSource( o.Self ), new CollisionSource( o.Other ) );
		foreach ( var e in CollisionEvents )
		{
			e.OnCollisionStop( c );
		}
	}

	void OnBodyOutOfBounds( PhysicsBody body )
	{
		var rb = body.Component as Rigidbody;
		if ( rb.IsValid() == false ) return;
		IScenePhysicsEvents.Post( x => x.OnOutOfBounds( rb ) );
	}

	void OnBodyFellAsleep( PhysicsBody body )
	{
		var rb = body.Component as Rigidbody;
		if ( rb.IsValid() == false ) return;
		IScenePhysicsEvents.Post( x => x.OnFellAsleep( rb ) );
	}

	void OnBodyActive( PhysicsBody body )
	{
		if ( body.Component is Rigidbody rb )
			MovedRigidbodies.Add( rb );
	}

	void DebugDrawPhysics()
	{
		if ( !PhysicsWorld.IsValid() )
			return;

		using ( Performance.Scope( "PhysicsDraw" ) )
		{
			PhysicsWorld.DebugDraw();
		}
	}

	internal void AddKeyframe( Collider collider ) => KeyframeColliders.Add( collider );
	internal void RemoveKeyframe( Collider collider ) => KeyframeColliders.Remove( collider );

	internal void AddRigidBody( Rigidbody rigidBody )
	{
		if ( !rigidBody.IsValid() )
			return;

		RigidBodies.Add( rigidBody );
		rigidBody.UpdateBody();
	}

	internal void RemoveRigidBody( Rigidbody rigidBody )
	{
		if ( !rigidBody.IsValid() )
			return;

		RigidBodies.Remove( rigidBody );
		rigidBody.UpdateBody();
	}

	internal void RemoveRigidBodies()
	{
		var bodies = RigidBodies.ToList();
		RigidBodies.Clear();

		foreach ( var rb in bodies )
			rb.UpdateBody();
	}

	internal bool HasRigidBody( Rigidbody rigidBody )
	{
		return RigidBodies.Contains( rigidBody );
	}
}
