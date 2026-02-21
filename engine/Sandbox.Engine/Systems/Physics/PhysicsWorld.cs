using NativeEngine;
using System.Runtime.InteropServices;
using static Sandbox.PhysicsWorld;

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
[Expose, ActionGraphIgnore]
public sealed partial class PhysicsWorld : IHandle
{
	[SkipHotload]
	internal static HashSet<PhysicsWorld> All = new HashSet<PhysicsWorld>();

	internal IPhysicsWorld native => world;
	internal IPhysicsWorld world;

	HashSet<PhysicsBody> bodies = new HashSet<PhysicsBody>();

	/// <summary>
	/// All bodies in the world
	/// </summary>
	public IEnumerable<PhysicsBody> Bodies => bodies.Where( x => x.IsValid() );

	//public Action<int, PhysicsBody, PhysicsBody, Vector3> Internal_OnCollision;

	/// <summary>
	/// Set or retrieve the collision rules for this <see cref="PhysicsWorld"/>.
	/// </summary>
	public CollisionRules CollisionRules { get; set; }

	void IHandle.HandleDestroy()
	{
		world = default;
		All.Remove( this );
	}
	void IHandle.HandleInit( IntPtr ptr )
	{
		world = ptr;
		world.SetWorldReferenceBody( new PhysicsBody( this ) );
		gravity = world.GetGravity();
		All.Add( this );
	}
	bool IHandle.HandleValid() => world.IsValid;
	internal PhysicsWorld( HandleCreationData _ ) { }

	internal bool IsTransient { get; set; }

	/// <summary>
	/// Create a new physics world. You should only do this if you want to simulate an extra world for some reason.
	/// </summary>
	public PhysicsWorld()
	{
		IsTransient = true;

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			NativeEngine.g_pPhysicsSystem.CreateWorld();
		}
	}

	/// <summary>
	/// Temp function for creating model physics until entity system handles it
	/// </summary>
	public PhysicsGroup SetupPhysicsFromModel( Model model, PhysicsMotionType motionType )
	{
		return native.CreateAggregateInstance( model.native, Transform.Zero, 0, motionType );
	}

	/// <summary>
	/// Temp function for creating model physics until entity system handles it
	/// </summary>
	public PhysicsGroup SetupPhysicsFromModel( Model model, Transform transform, PhysicsMotionType motionType )
	{
		return native.CreateAggregateInstance( model.native, transform, 0, motionType );
	}

	/// <summary>
	/// Delete this world and all objects inside. Will throw an exception if you try to delete a world that you didn't manually create.
	/// </summary>
	public void Delete()
	{
		Assert.True( IsTransient );
		if ( !world.IsValid ) return;
		NativeEngine.g_pPhysicsSystem.DestroyWorld( this );
	}

	/// <summary>
	/// Step simulation of this physics world. You can only do this on physics worlds that you manually create.
	/// </summary>
	public void Step( float delta ) => Step( delta, 1 );

	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	unsafe delegate void ProcessIntersectionsDelegate_t( VPhysIntersectionNotification_t* ptr );

	internal float CurrentTime;
	internal float CurrentDelta;

	/// <summary>
	/// Step simulation of this physics world. You can only do this on physics worlds that you manually create.
	/// </summary>
	public unsafe void Step( float delta, int subSteps )
	{
		CurrentTime += delta;
		Step( CurrentTime, delta, subSteps * SubSteps );
	}

	/// <summary>
	/// Step simulation of this physics world. You can only do this on physics worlds that you manually create.
	/// </summary>
	public void Step( float worldTime, float delta, int subSteps )
	{
		Assert.True( IsTransient, "You can only step simulation of physics worlds that you create" );
		if ( !world.IsValid ) return;

		UpdateCollisionRulesHash();

		CurrentTime = worldTime;
		CurrentDelta = delta;

		world.StepSimulation( delta, subSteps * SubSteps );

		ProcessIntersections();
	}

	private int _collisionRulesHash;
	private void UpdateCollisionRulesHash()
	{
		if ( CollisionRules is null )
			return;

		var hash = CollisionRules.GetHashCode();
		if ( _collisionRulesHash == hash )
			return;

		var json = Json.SerializeAsObject( CollisionRules );
		world.SetCollisionRulesFromJson( json.ToJsonString() );
		_collisionRulesHash = hash;
	}

	DelegateFunctionPointer onIntersectionFunctionPointer;

	internal unsafe void ProcessIntersections()
	{
		// I wonder if this is slow and we should cache it?
		if ( onIntersectionFunctionPointer == DelegateFunctionPointer.Null )
			onIntersectionFunctionPointer = DelegateFunctionPointer.Get<ProcessIntersectionsDelegate_t>( OnIntersection );

		world.ProcessIntersections( onIntersectionFunctionPointer );
	}

	//-------------------------------------------------------------------------------------------
	// This is being done this way to provide a uniform and easy to debug initial API.  Fast paths will be needed.
	internal struct VPhysIntersectionNotification_t
	{
		public IntersectionEventType_t Reason;

		public Side Left;
		public Side Right;

		public Vector3 ContactPoint;
		public Vector3 ContactSpeed;
		public Vector3 SurfaceNormal;
		public float ContactNormalSpeed;
		public float Impulse;

		public struct Side
		{
			public IPhysicsShape Shape;
			public IPhysicsBody Body;

			public int SurfaceIndex;
		};
	}

	internal enum IntersectionEventType_t
	{
		TouchBegin,
		TouchEnd,
		TouchPersists,
		Hit,
		TriggerBegin,
		TriggerEnd,
	}

	internal Action<PhysicsIntersection> OnIntersectionStart { get; set; }
	internal Action<PhysicsIntersection> OnIntersectionHit { get; set; }
	internal Action<PhysicsIntersectionEnd> OnIntersectionEnd { get; set; }
	internal Action<PhysicsIntersection> OnIntersectionUpdate { get; set; }
	internal Action<PhysicsBody> OnBodyOutOfBounds { get; set; }

	unsafe void OnIntersection( VPhysIntersectionNotification_t* ptr )
	{
		try
		{
			var c = new PhysicsContact( ptr );
			var a = new PhysicsContact.Target( ptr->Left );
			var b = new PhysicsContact.Target( ptr->Right );

			Assert.NotNull( a.Body, "a.Body was null.. does this make any sense?" );
			Assert.NotNull( b.Body, "b.Body was null.. does this make any sense?" );

			if ( ptr->Reason == IntersectionEventType_t.TouchBegin )
			{
				OnIntersectionStart?.InvokeWithWarning( new PhysicsIntersection( a, b, c ) );

				a.Body.OnIntersectionStart?.InvokeWithWarning( new PhysicsIntersection( a, b, c ) );
				b.Body.OnIntersectionStart?.InvokeWithWarning( new PhysicsIntersection( b, a, c ) );
			}
			else if ( ptr->Reason == IntersectionEventType_t.Hit )
			{
				OnIntersectionHit?.InvokeWithWarning( new PhysicsIntersection( a, b, c ) );
			}
			else if ( ptr->Reason == IntersectionEventType_t.TouchEnd )
			{
				OnIntersectionEnd?.InvokeWithWarning( new PhysicsIntersectionEnd( a, b ) );

				a.Body.OnIntersectionEnd?.InvokeWithWarning( new PhysicsIntersectionEnd( a, b ) );
				b.Body.OnIntersectionEnd?.InvokeWithWarning( new PhysicsIntersectionEnd( b, a ) );
			}
			else if ( ptr->Reason == IntersectionEventType_t.TouchPersists )
			{
				OnIntersectionUpdate?.InvokeWithWarning( new PhysicsIntersection( a, b, c ) );

				a.Body.OnIntersectionUpdate?.InvokeWithWarning( new PhysicsIntersection( a, b, c ) );
				b.Body.OnIntersectionUpdate?.InvokeWithWarning( new PhysicsIntersection( b, a, c ) );
			}
			else if ( ptr->Reason == IntersectionEventType_t.TriggerBegin )
			{
				a.Body.OnTriggerBegin?.InvokeWithWarning( new PhysicsIntersection( a, b, c ) );
				b.Body.OnTriggerBegin?.InvokeWithWarning( new PhysicsIntersection( b, a, c ) );
			}
			else if ( ptr->Reason == IntersectionEventType_t.TriggerEnd )
			{
				a.Body.OnTriggerEnd?.InvokeWithWarning( new PhysicsIntersectionEnd( a, b ) );
				b.Body.OnTriggerEnd?.InvokeWithWarning( new PhysicsIntersectionEnd( b, a ) );
			}
		}
		catch ( System.Exception e )
		{
			Log.Error( e );
		}

	}

	Vector3 gravity;

	/// <summary>
	/// Access the world's current gravity.
	/// </summary>
	[ActionGraphInclude]
	public Vector3 Gravity
	{
		get => gravity;
		set
		{
			if ( gravity == value ) return;

			gravity = value;
			world.SetGravity( gravity );
		}
	}

	float airDensity;

	/// <summary>
	/// Air density of this physics world, for things like air drag.
	/// </summary>
	[ActionGraphInclude]
	public float AirDensity
	{
		get => airDensity;
		set
		{
			if ( airDensity == value ) return;

			airDensity = value;
		}
	}

	PhysicsBody _cachedWorldBody;

	/// <summary>
	/// The body of this physics world.
	/// </summary>
	[ActionGraphInclude]
	public PhysicsBody Body
	{
		get
		{
			if ( !_cachedWorldBody.IsValid() )
			{
				_cachedWorldBody = native.GetWorldReferenceBody();
			}

			return _cachedWorldBody;
		}
	}

	PhysicsGroup _cachedGroup;

	/// <summary>
	/// The physics group of this physics world. A physics world will contain only 1 body.
	/// </summary>
	[ActionGraphInclude]
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
	/// If true then bodies will be able to sleep after a period of inactivity
	/// </summary>
	public bool SleepingEnabled
	{
		get => world.IsSleepingEnabled();
		set
		{
			if ( value ) world.EnableSleeping();
			else world.DisableSleeping();
		}
	}

	internal float MaximumLinearSpeed
	{
		set => world.SetMaximumLinearSpeed( value );
	}

	/// <summary>
	/// Physics simulation mode. See <see cref="PhysicsSimulationMode"/> for explanation of each mode.
	/// </summary>
	public PhysicsSimulationMode SimulationMode
	{
		get => world.GetSimulation();
		set => world.SetSimulation( value );
	}

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

	internal void RegisterBody( PhysicsBody physicsBody )
	{
		bodies.Add( physicsBody );
	}

	internal void UnregisterBody( PhysicsBody physicsBody )
	{
		world.RemoveBody( physicsBody );
		bodies.Remove( physicsBody );
	}
}

[Expose]
public readonly unsafe struct PhysicsContact
{
	internal PhysicsContact( VPhysIntersectionNotification_t* ptr )
	{
		Point = ptr->ContactPoint;
		Speed = ptr->ContactSpeed;
		Normal = ptr->SurfaceNormal;
		NormalSpeed = ptr->ContactNormalSpeed;
		Impulse = ptr->Impulse;
	}

	public readonly Vector3 Point;
	public readonly Vector3 Speed;
	public readonly Vector3 Normal;
	public readonly float NormalSpeed;
	public readonly float Impulse;

	[Expose]
	public readonly struct Target
	{
		internal Target( in VPhysIntersectionNotification_t.Side o )
		{
			Assert.True( o.Body.IsValid );
			Assert.True( o.Shape.IsValid );

			Body = o.Body.ManagedObject();
			Shape = o.Shape.ManagedObject();
			Surface = Surface.FindByIndex( o.SurfaceIndex );
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
