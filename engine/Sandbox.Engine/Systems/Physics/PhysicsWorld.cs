namespace Sandbox;

/// <summary>
/// Physics simulation mode. For use with <see cref="PhysicsWorld.SimulationMode"/>.
/// </summary>
public enum PhysicsSimulationMode
{
	/// <summary>
	/// Discrete collision detection.
	/// In this mode physics bodies can fly through thin walls when moving very quickly, but it is has better performance.
	/// </summary>
	Discrete,

	/// <summary>
	/// Continuous collision detection. This is the default mode.
	/// </summary>
	Continuous
};

/// <summary>
/// A world in which physics objects exist. You can create your own world but you really don't need to. A world for the map is created clientside and serverside automatically.
/// </summary>
[Expose]
public sealed partial class PhysicsWorld : IValid
{
	internal PhysicsWorldInternal _world;

	internal PhysicsWorld( PhysicsWorldInternal world )
	{
		_world = world;
		world.SetOwner( this );
	}

	/// <summary>
	/// Creates a new 3D physics world.
	/// </summary>
	public PhysicsWorld()
	{
		_world = new PhysicsWorld3d();
		_world.SetOwner( this );
	}

	/// <summary>
	/// Creates a new 3D physics world.
	/// </summary>
	[Obsolete( "Use new PhysicsWorld() instead" )]
	public static PhysicsWorld Create() => new PhysicsWorld();

	public bool IsValid => _world is not null && _world.IsValid;

	/// <summary>
	/// Create a new physics body in this world.
	/// </summary>
	public PhysicsBody CreateBody() => _world.CreateBody();

	internal PhysicsBodyInternal CreateBodyInternal() => _world.CreateBodyInternal();

	/// <summary>
	/// The world's static reference body, used as a default anchor for joints.
	/// </summary>
	public PhysicsBody Body => _world.Body;

	/// <summary>
	/// All bodies in the world
	/// </summary>
	public IEnumerable<PhysicsBody> Bodies => _world.Bodies;

	internal int BodyCount => _world.BodyCount;

	/// <summary>
	/// Set or retrieve the collision rules for this <see cref="PhysicsWorld"/>.
	/// </summary>
	public CollisionRules CollisionRules
	{
		get => _world.CollisionRules;
		set => _world.CollisionRules = value;
	}

	internal bool IsTransient
	{
		get => _world.IsTransient;
		set => _world.IsTransient = value;
	}

	internal Scene Scene
	{
		get => _world.Scene;
		set => _world.Scene = value;
	}

	internal double CurrentTime => _world.CurrentTime;
	internal float CurrentDelta => _world.CurrentDelta;

	/// <summary>
	/// Delete this world and all objects inside. Will throw an exception if you try to delete a world that you didn't manually create.
	/// </summary>
	public void Delete() => _world.Delete();

	/// <summary>
	/// Step simulation of this physics world. You can only do this on physics worlds that you manually create.
	/// </summary>
	public void Step( float delta ) => _world.Step( delta );

	/// <summary>
	/// Step simulation of this physics world. You can only do this on physics worlds that you manually create.
	/// </summary>
	public void Step( float delta, int subSteps ) => _world.Step( delta, subSteps );

	/// <summary>
	/// Step simulation of this physics world. You can only do this on physics worlds that you manually create.
	/// </summary>
	public void Step( double worldTime, float delta, int subSteps ) => _world.Step( worldTime, delta, subSteps );

	/// <summary>
	/// Access the world's current gravity.
	/// </summary>
	public Vector3 Gravity
	{
		get => _world.Gravity;
		set => _world.Gravity = value;
	}

	/// <summary>
	/// If true then bodies will be able to sleep after a period of inactivity
	/// </summary>
	public bool SleepingEnabled
	{
		get => _world.SleepingEnabled;
		set => _world.SleepingEnabled = value;
	}

	internal float MaximumLinearSpeed
	{
		set => _world.MaximumLinearSpeed = value;
	}

	/// <summary>
	/// Physics simulation mode. See <see cref="PhysicsSimulationMode"/> for explanation of each mode.
	/// </summary>
	public PhysicsSimulationMode SimulationMode
	{
		get => _world.SimulationMode;
		set => _world.SimulationMode = value;
	}

	[Obsolete]
	public int PositionIterations { get => 0; set { } }

	[Obsolete]
	public int VelocityIterations { get => 0; set { } }

	/// <summary>
	/// If you're seeing objects go through other objects or you have a low tickrate, you might want to increase the number of physics substeps.
	/// This breaks physics steps down into this many substeps. The default is 1 and works pretty good.
	/// Be aware that the number of physics ticks per second is going to be tickrate * substeps.
	/// So if you're ticking at 90 and you have SubSteps set to 1000 then you're going to do 90,000 steps per second. So be careful here.
	/// </summary>
	public int SubSteps
	{
		get => _world.SubSteps;
		set => _world.SubSteps = value;
	}

	[Obsolete]
	public float TimeScale { get; set; }

	/// <summary>
	/// Air density of this physics world, for things like air drag.
	/// </summary>
	public float AirDensity
	{
		get => _world.AirDensity;
		set => _world.AirDensity = value;
	}

	/// <summary>
	/// The physics group of this physics world. A physics world will contain only 1 body.
	/// </summary>
	public PhysicsGroup Group => _world.Group;

	/// <summary>
	/// Used internally to set collision rules from gamemode's project settings.
	/// You shouldn't need to call this yourself.
	/// </summary>
	[Obsolete( "Use CollisionRules Property" )]
	public void SetCollisionRules( CollisionRules rules ) => _world.CollisionRules = rules;

	/// <summary>
	/// Gets the specific collision rule for a pair of tags.
	/// </summary>
	public CollisionRules.Result GetCollisionRule( string left, string right ) => _world.GetCollisionRule( left, right );

	/// <summary>
	/// Raytrace against this world
	/// </summary>
	public PhysicsTraceBuilder Trace => _world.Trace;

	/// <summary>
	/// Like calling PhysicsTraceBuilder.Run, except will re-target this world if it's not already the target
	/// </summary>
	public PhysicsTraceResult RunTrace( in PhysicsTraceBuilder trace ) => _world.RunTrace( trace );

	/// <summary>
	/// Like calling PhysicsTraceBuilder.RunAll, except will re-target this world if it's not already the target
	/// </summary>
	public PhysicsTraceResult[] RunTraceAll( in PhysicsTraceBuilder trace ) => _world.RunTraceAll( trace );

	/// <summary>
	/// Find game objects overlapping a sphere.
	/// </summary>
	public IEnumerable<GameObject> FindInPhysics( Sphere sphere ) => _world.FindInPhysics( sphere );

	/// <summary>
	/// Find game objects overlapping a box.
	/// </summary>
	public IEnumerable<GameObject> FindInPhysics( BBox box ) => _world.FindInPhysics( box );

	/// <summary>
	/// Find game objects inside a frustum.
	/// </summary>
	public IEnumerable<GameObject> FindInPhysics( Frustum frustum ) => _world.FindInPhysics( frustum );

	/// <summary>
	/// Temp function for creating model physics until entity system handles it
	/// </summary>
	public PhysicsGroup SetupPhysicsFromModel( Model model, PhysicsMotionType motionType ) => _world.SetupPhysicsFromModel( model, motionType );

	/// <summary>
	/// Temp function for creating model physics until entity system handles it
	/// </summary>
	public PhysicsGroup SetupPhysicsFromModel( Model model, Transform transform, PhysicsMotionType motionType ) => _world.SetupPhysicsFromModel( model, transform, motionType );

	/// <summary>
	/// A SceneWorld where debug SceneObjects exist.
	/// </summary>
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public SceneWorld DebugSceneWorld
	{
		get => _world.DebugSceneWorld;
		set => _world.DebugSceneWorld = value;
	}

	/// <summary>
	/// Updates all the SceneObjects in the <see cref="DebugSceneWorld"/>, call once per tick or frame.
	/// </summary>
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public void DebugDraw() => _world.DebugDraw();

	internal void RegisterBody( PhysicsBody body ) => _world.RegisterBody( body._body );
	internal void UnregisterBody( PhysicsBody body ) => _world.UnregisterBody( body._body );
	internal void OnBodyDestroyed( PhysicsBody body ) => _world.OnBodyDestroyed( body._body );

	internal Physics.FixedJoint CreateWeldJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 ) => _world.CreateWeldJoint( body1, body2, localFrame1, localFrame2 );
	internal Physics.SpringJoint CreateSpringJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 ) => _world.CreateSpringJoint( body1, body2, localFrame1, localFrame2 );
	internal Physics.HingeJoint CreateRevoluteJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 ) => _world.CreateRevoluteJoint( body1, body2, localFrame1, localFrame2 );
	internal Physics.SliderJoint CreatePrismaticJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 ) => _world.CreatePrismaticJoint( body1, body2, localFrame1, localFrame2 );
	internal Physics.BallSocketJoint CreateSphericalJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 ) => _world.CreateSphericalJoint( body1, body2, localFrame1, localFrame2 );
	internal Physics.ControlJoint CreateMotorJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 ) => _world.CreateMotorJoint( body1, body2, localFrame1, localFrame2 );
	internal Physics.WheelJoint CreateWheelJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 ) => _world.CreateWheelJoint( body1, body2, localFrame1, localFrame2 );
	internal Physics.UprightJoint CreateParallelJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 ) => _world.CreateParallelJoint( body1, body2, localFrame1, localFrame2 );
	internal PhysicsJoint CreateFilterJoint( PhysicsBody body1, PhysicsBody body2 ) => _world.CreateFilterJoint( body1, body2 );
}

[Expose]
public readonly unsafe struct PhysicsContact
{
	internal PhysicsContact( PhysicsWorld3d.VPhysIntersectionNotification_t* ptr )
	{
		Point = ptr->ContactPoint;
		Speed = ptr->ContactSpeed;
		Normal = ptr->SurfaceNormal;
		NormalSpeed = ptr->ContactNormalSpeed;
		Impulse = ptr->Impulse;
	}

	internal PhysicsContact( Vector3 point, Vector3 normal, float normalSpeed, float impulse )
	{
		Point = point;
		Normal = normal;
		NormalSpeed = normalSpeed;
		Speed = normal * normalSpeed;
		Impulse = impulse;
	}

	public readonly Vector3 Point;
	public readonly Vector3 Speed;
	public readonly Vector3 Normal;
	public readonly float NormalSpeed;
	public readonly float Impulse;

	[Expose]
	public readonly struct Target
	{
		internal Target( in PhysicsWorld3d.VPhysIntersectionNotification_t.Side o )
		{
			Body = HandleIndex.Get<PhysicsBody3d>( o.BodyManagedIndex )?.Owner;
			Shape = HandleIndex.Get<PhysicsShape3d>( o.ShapeManagedIndex )?.Owner;
			Surface = Surface.FindByIndex( o.SurfaceIndex );
		}

		internal Target( PhysicsBody body, PhysicsShape shape, Surface surface )
		{
			Body = body;
			Shape = shape;
			Surface = surface;
		}

		public readonly PhysicsBody Body;
		public readonly PhysicsShape Shape;
		public readonly Surface Surface;
	}
}

[Expose]
public readonly record struct PhysicsIntersection( PhysicsContact.Target Self, PhysicsContact.Target Other, PhysicsContact Contact );

[Expose]
public readonly record struct PhysicsIntersectionEnd( PhysicsContact.Target Self, PhysicsContact.Target Other );
