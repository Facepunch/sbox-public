namespace Sandbox;

/// <summary>
/// Ticks the 2D physics (Box2D) in FrameStage.PhysicsStep.
/// </summary>
[Expose]
sealed partial class ScenePhysics2dSystem : GameObjectSystem<ScenePhysics2dSystem>
{
	internal PhysicsWorld2d World => Scene.HasPhysicsWorld ? Scene.PhysicsWorld?._world as PhysicsWorld2d : null;

	private List<ISceneCollisionEvents> CollisionEvents { get; } = new();
	private HashSet<Collider> KeyframeColliders { get; } = new();

	internal bool Enabled { get; set; }

	internal void AddKeyframe( Collider collider ) => KeyframeColliders.Add( collider );
	internal void RemoveKeyframe( Collider collider ) => KeyframeColliders.Remove( collider );

	public ScenePhysics2dSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.PhysicsStep, 0, UpdatePhysics, "UpdatePhysics" );
		Listen( Stage.StartUpdate, 1000, DrawDebug, "DrawDebug2D" );
	}

	public override void Dispose()
	{
		base.Dispose();

		KeyframeColliders.Clear();
	}

	void UpdatePhysics()
	{
		if ( !Scene.Is2D )
			return;

		if ( Scene.IsEditor && !Enabled )
			return;

		var world = World;
		if ( !world.IsValid() )
			return;

		using var _ = PerformanceStats.Timings.Physics.Scope();

		IScenePhysicsEvents.Post( x => x.PrePhysicsStep() );

		UpdateKeyframeTransforms();

		world.Step( Time.Delta, 1 );

		CollisionEvents.Clear();
		Scene.GetAll( CollisionEvents );

		ProcessContactEvents();
		ProcessSensorEvents();

		foreach ( var rb in Scene.GetAll<Rigidbody>() )
		{
			rb.UpdateTransformFromBody();
		}

		IScenePhysicsEvents.Post( x => x.PostPhysicsStep() );
	}

	void UpdateKeyframeTransforms()
	{
		if ( Time.Delta <= 0f ) return;

		foreach ( var collider in KeyframeColliders )
		{
			if ( !collider.IsValid() || !collider.Active ) continue;

			collider.UpdateKeyframeTransform();
		}
	}
}
