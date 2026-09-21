namespace Sandbox;

/// <summary>
/// Internal base class for physics worlds. Contains all world functions and properties.
/// The public <see cref="PhysicsWorld"/> wrapper delegates to an instance of this (2D or 3D).
/// </summary>
internal abstract partial class PhysicsWorldInternal : IValid
{
	[SkipHotload]
	internal static HashSet<PhysicsWorldInternal> All = new HashSet<PhysicsWorldInternal>();

	PhysicsWorld _owner;

	/// <summary>
	/// The public wrapper for this world. Created lazily for worlds that originate native-side.
	/// </summary>
	internal PhysicsWorld Owner => _owner ??= new PhysicsWorld( this );

	/// <summary>
	/// Associates this internal world with its public wrapper (only sets it once).
	/// </summary>
	internal void SetOwner( PhysicsWorld owner ) => _owner ??= owner;

	public abstract bool IsValid { get; }

	/// <summary>
	/// Create a new physics body in this world.
	/// </summary>
	internal PhysicsBody CreateBody() => CreateBodyInternal().Owner;

	/// <summary>
	/// Create the internal implementation body for this world (2D or 3D).
	/// </summary>
	internal abstract PhysicsBodyInternal CreateBodyInternal();

	/// <summary>
	/// The world's static reference body, used as a default anchor for joints.
	/// </summary>
	public virtual PhysicsBody Body => null;

	HashSet<PhysicsBodyInternal> bodies = new HashSet<PhysicsBodyInternal>();

	/// <summary>
	/// All bodies in the world
	/// </summary>
	public IEnumerable<PhysicsBody> Bodies => bodies.Where( x => x.IsValid ).Select( x => x.Owner );

	internal int BodyCount => bodies.Count;

	/// <summary>
	/// Set or retrieve the collision rules for this <see cref="PhysicsWorld"/>.
	/// </summary>
	public CollisionRules CollisionRules { get; set; }

	internal bool IsTransient { get; set; }

	/// <summary>
	/// Delete this world and all objects inside. Will throw an exception if you try to delete a world that you didn't manually create.
	/// </summary>
	public virtual void Delete() { }

	/// <summary>
	/// Step simulation of this physics world. You can only do this on physics worlds that you manually create.
	/// </summary>
	public virtual void Step( float delta ) => Step( delta, 1 );

	internal double CurrentTime;
	internal float CurrentDelta;

	/// <summary>
	/// Step simulation of this physics world. You can only do this on physics worlds that you manually create.
	/// </summary>
	public virtual void Step( float delta, int subSteps )
	{
		CurrentTime += delta;
		Step( CurrentTime, delta, subSteps * SubSteps );
	}

	/// <summary>
	/// Step simulation of this physics world. You can only do this on physics worlds that you manually create.
	/// </summary>
	public virtual void Step( double worldTime, float delta, int subSteps )
	{
		CurrentTime = worldTime;
		CurrentDelta = delta;
	}

	protected Vector3 gravity;

	/// <summary>
	/// Access the world's current gravity.
	/// </summary>
	public virtual Vector3 Gravity
	{
		get => gravity;
		set => gravity = value;
	}

	/// <summary>
	/// If true then bodies will be able to sleep after a period of inactivity
	/// </summary>
	public virtual bool SleepingEnabled { get; set; }

	internal virtual float MaximumLinearSpeed { set { } }

	/// <summary>
	/// Physics simulation mode. See <see cref="PhysicsSimulationMode"/> for explanation of each mode.
	/// </summary>
	public virtual PhysicsSimulationMode SimulationMode { get; set; }

	[Obsolete]
	public int PositionIterations
	{
		get => 0;
		set
		{
		}
	}

	[Obsolete]
	public int VelocityIterations
	{
		get => 0;
		set
		{
		}
	}

	/// <summary>
	/// If you're seeing objects go through other objects or you have a low tickrate, you might want to increase the number of physics substeps.
	/// This breaks physics steps down into this many substeps. The default is 1 and works pretty good.
	/// Be aware that the number of physics ticks per second is going to be tickrate * substeps.
	/// So if you're ticking at 90 and you have SubSteps set to 1000 then you're going to do 90,000 steps per second. So be careful here.
	/// </summary>
	public int SubSteps { get; set; } = 1;

	[Obsolete]
	public float TimeScale { get; set; }

	/// <summary>
	/// Used internally to set collision rules from gamemode's project settings.
	/// You shouldn't need to call this yourself.
	/// </summary>
	[Obsolete( "Use CollisionRules Property" )]
	public void SetCollisionRules( CollisionRules rules )
	{
		CollisionRules = rules;
	}

	/// <summary>
	/// Gets the specific collision rule for a pair of tags.
	/// </summary>
	public CollisionRules.Result GetCollisionRule( string left, string right )
	{
		return CollisionRules.GetCollisionRule( left, right );
	}

	/// <summary>
	/// Raytrace against this world
	/// </summary>
	public PhysicsTraceBuilder Trace
	{
		get
		{
			return new PhysicsTraceBuilder( this );
		}
	}

	/// <summary>
	/// Like calling PhysicsTraceBuilder.Run, except will re-target this world if it's not already the target
	/// </summary>
	public PhysicsTraceResult RunTrace( in PhysicsTraceBuilder trace )
	{
		var newTrace = Trace;
		newTrace.request = trace.request;
		newTrace.targetBody = trace.targetBody;
		newTrace.filterCallback = trace.filterCallback;
		return newTrace.Run();
	}

	/// <summary>
	/// Like calling PhysicsTraceBuilder.RunAll, except will re-target this world if it's not already the target
	/// </summary>
	public PhysicsTraceResult[] RunTraceAll( in PhysicsTraceBuilder trace )
	{
		var newTrace = Trace;
		newTrace.request = trace.request;
		newTrace.targetBody = trace.targetBody;
		newTrace.filterCallback = trace.filterCallback;
		return newTrace.RunAll();
	}

	/// <summary>
	/// Run a trace against this world and return the closest hit. Thread-safe.
	/// </summary>
	internal virtual PhysicsTraceResult TraceSingle( in PhysicsTrace.Request request, PhysicsBody targetBody, Func<PhysicsShape, bool> filterCallback )
	{
		return new PhysicsTraceResult
		{
			Hit = false,
			Fraction = 1f,
			StartPosition = request.StartPos,
			EndPosition = request.EndPos,
			HitPosition = request.EndPos,
			Direction = (request.EndPos - request.StartPos).Normal,
			StartShape = request.StartShape
		};
	}

	/// <summary>
	/// Run a trace against this world and return all hits. Thread-safe.
	/// </summary>
	internal virtual PhysicsTraceResult[] TraceMultiple( in PhysicsTrace.Request request, PhysicsBody targetBody, Func<PhysicsShape, bool> filterCallback ) => [];

	/// <summary>
	/// Run a trace against this world, appending every hit to <paramref name="results"/> and returning the hit count. Thread-safe.
	/// </summary>
	internal virtual int TraceMultiple( in PhysicsTrace.Request request, PhysicsBody targetBody, Func<PhysicsShape, bool> filterCallback, List<PhysicsTraceResult> results ) => 0;

	/// <summary>
	/// Find game objects overlapping a sphere.
	/// </summary>
	public virtual IEnumerable<GameObject> FindInPhysics( Sphere sphere ) => Enumerable.Empty<GameObject>();

	/// <summary>
	/// Find game objects overlapping a box.
	/// </summary>
	public virtual IEnumerable<GameObject> FindInPhysics( BBox box ) => Enumerable.Empty<GameObject>();

	/// <summary>
	/// Find game objects inside a frustum.
	/// </summary>
	public virtual IEnumerable<GameObject> FindInPhysics( Frustum frustum ) => Enumerable.Empty<GameObject>();

	/// <summary>
	/// Find physics bodies overlapping a sphere, writing distinct bodies into a caller-provided span. Returns the number written.
	/// </summary>
	public virtual int FindBodiesInPhysics( Vector3 center, float radius, Span<PhysicsBody> result ) => 0;

	float airDensity;

	/// <summary>
	/// Air density of this physics world, for things like air drag.
	/// </summary>
	public virtual float AirDensity
	{
		get => airDensity;
		set => airDensity = value;
	}

	PhysicsGroup _cachedGroup;

	/// <summary>
	/// The physics group of this physics world. A physics world will contain only 1 body.
	/// </summary>
	public PhysicsGroup Group
	{
		get
		{
			if ( !_cachedGroup.IsValid() )
			{
				_cachedGroup = Body?.PhysicsGroup ?? null;
			}

			return _cachedGroup;
		}
	}

	/// <summary>
	/// Temp function for creating model physics until entity system handles it
	/// </summary>
	public virtual PhysicsGroup SetupPhysicsFromModel( Model model, PhysicsMotionType motionType ) => throw new NotSupportedException();

	/// <summary>
	/// Temp function for creating model physics until entity system handles it
	/// </summary>
	public virtual PhysicsGroup SetupPhysicsFromModel( Model model, Transform transform, PhysicsMotionType motionType ) => throw new NotSupportedException();

	internal void RegisterBody( PhysicsBodyInternal physicsBody )
	{
		bodies.Add( physicsBody );
	}

	internal virtual void UnregisterBody( PhysicsBodyInternal physicsBody )
	{
		bodies.Remove( physicsBody );
	}

	internal void OnBodyDestroyed( PhysicsBodyInternal physicsBody ) => bodies.Remove( physicsBody );

	// ── Joint Creation ──────────────────────────────────────────────

	/// <summary>
	/// Creates an almost solid constraint between two physics bodies.
	/// </summary>
	internal abstract Physics.FixedJoint CreateWeldJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );

	/// <summary>
	/// Creates a spring/distance constraint between two physics bodies.
	/// </summary>
	internal abstract Physics.SpringJoint CreateSpringJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );

	/// <summary>
	/// Creates a revolute/hinge constraint between two physics bodies.
	/// </summary>
	internal abstract Physics.HingeJoint CreateRevoluteJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );

	/// <summary>
	/// Creates a prismatic/slider constraint between two physics bodies.
	/// </summary>
	internal abstract Physics.SliderJoint CreatePrismaticJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );

	/// <summary>
	/// Creates a spherical/ball-socket constraint between two physics bodies.
	/// </summary>
	internal abstract Physics.BallSocketJoint CreateSphericalJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );

	/// <summary>
	/// Creates a motor/control constraint between two physics bodies.
	/// </summary>
	internal abstract Physics.ControlJoint CreateMotorJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );

	/// <summary>
	/// Creates a wheel constraint between two physics bodies.
	/// </summary>
	internal abstract Physics.WheelJoint CreateWheelJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );

	/// <summary>
	/// Creates a parallel/upright constraint between two physics bodies.
	/// </summary>
	internal abstract Physics.UprightJoint CreateParallelJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );

	/// <summary>
	/// Creates a filter joint between two physics bodies (controls collision filtering).
	/// </summary>
	internal abstract PhysicsJoint CreateFilterJoint( PhysicsBody body1, PhysicsBody body2 );
}
