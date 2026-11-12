using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// 3D physics world backed by a native Box3D/Source 2 <see cref="IPhysicsWorld"/> handle.
/// </summary>
internal sealed partial class PhysicsWorld3d : PhysicsWorldInternal, IHandle
{
	internal IPhysicsWorld native => world;
	internal IPhysicsWorld world;

	void IHandle.HandleInit( IntPtr ptr )
	{
		world = ptr;
		world.SetWorldReferenceBody( new PhysicsBody3d( this ) );
		gravity = world.GetGravity();
		All.Add( this );
	}

	void IHandle.HandleDestroy()
	{
		world = default;
		All.Remove( this );
	}

	bool IHandle.HandleValid() => world.IsValid;

	public override bool IsValid => world.IsValid;

	internal PhysicsWorld3d( HandleCreationData _ ) { }

	/// <summary>
	/// Create a new physics world. You should only do this if you want to simulate an extra world for some reason.
	/// </summary>
	public PhysicsWorld3d()
	{
		IsTransient = true;

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			NativeEngine.g_pPhysicsSystem.CreateWorld();
		}
	}

	internal override PhysicsBodyInternal CreateBodyInternal() => new PhysicsBody3d( this );
	internal override PhysicsBodyInternal CreateBodyInternal( in PhysicsBodySettings settings ) => new PhysicsBody3d( this, settings );

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

			// HandleIndex values filled by native so we don't have to call back to resolve them
			public int ShapeManagedIndex;
			public int BodyManagedIndex;
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

	internal Action<Collision> OnIntersectionStart { get; set; }
	internal Action<Collision> OnIntersectionHit { get; set; }
	internal Action<CollisionStop> OnIntersectionEnd { get; set; }
	internal Action<Collision> OnIntersectionUpdate { get; set; }
	internal Action<PhysicsBody> OnBodyOutOfBounds { get; set; }
	internal Action<PhysicsBody> OnBodyFellAsleep { get; set; }

	PhysicsBody _cachedWorldBody;

	/// <summary>
	/// The body of this physics world.
	/// </summary>
	public override PhysicsBody Body
	{
		get
		{
			if ( !_cachedWorldBody.IsValid() )
			{
				_cachedWorldBody = native.GetWorldReferenceBody()?.Owner;
			}

			return _cachedWorldBody;
		}
	}

	/// <summary>
	/// Delete this world and all objects inside. Will throw an exception if you try to delete a world that you didn't manually create.
	/// </summary>
	public override void Delete()
	{
		Assert.True( IsTransient );
		if ( !world.IsValid ) return;
		NativeEngine.g_pPhysicsSystem.DestroyWorld( this );
	}

	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	unsafe delegate void ProcessIntersectionsDelegate_t( VPhysIntersectionNotification_t* notifications, int count );

	/// <summary>
	/// Step simulation of this physics world. You can only do this on physics worlds that you manually create.
	/// </summary>
	public override void Step( float delta, int subSteps )
	{
		CurrentTime += delta;
		Step( CurrentTime, delta, subSteps );
	}

	/// <summary>
	/// Step simulation of this physics world. You can only do this on physics worlds that you manually create.
	/// </summary>
	public override void Step( double worldTime, float delta, int subSteps )
	{
		Assert.True( IsTransient, "You can only step simulation of physics worlds that you create" );
		if ( !world.IsValid ) return;

		UpdateCollisionRulesHash();

		base.Step( worldTime, delta, subSteps );

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
		if ( onIntersectionFunctionPointer == DelegateFunctionPointer.Null )
			onIntersectionFunctionPointer = DelegateFunctionPointer.Get<ProcessIntersectionsDelegate_t>( OnIntersections );

		world.ProcessIntersections( onIntersectionFunctionPointer );
	}

	unsafe void OnIntersections( VPhysIntersectionNotification_t* notifications, int count )
	{
		for ( int i = 0; i < count; i++ )
		{
			try
			{
				var ptr = notifications + i;

				var a = new PhysicsContact.Target( ptr->Left );
				var b = new PhysicsContact.Target( ptr->Right );

				// A handler earlier in the batch may have deleted one of these, skip the event like native used to
				if ( !a.Body.IsValid() || !b.Body.IsValid() || !a.Shape.IsValid() || !b.Shape.IsValid() )
					continue;

				var c = new PhysicsContact( ptr );

				// Resolve each side once, the scene and both bodies get the same sources
				var sa = new CollisionSource( a );
				var sb = new CollisionSource( b );

				if ( ptr->Reason == IntersectionEventType_t.TouchBegin )
				{
					var ab = new Collision( sa, sb, c );
					OnIntersectionStart?.InvokeWithWarning( ab );
					a.Body.DispatchIntersectionStart( ab );
					b.Body.DispatchIntersectionStart( new Collision( sb, sa, c ) );
				}
				else if ( ptr->Reason == IntersectionEventType_t.Hit )
				{
					OnIntersectionHit?.InvokeWithWarning( new Collision( sa, sb, c ) );
				}
				else if ( ptr->Reason == IntersectionEventType_t.TouchEnd )
				{
					var ab = new CollisionStop( sa, sb );
					OnIntersectionEnd?.InvokeWithWarning( ab );
					a.Body.DispatchIntersectionEnd( ab );
					b.Body.DispatchIntersectionEnd( new CollisionStop( sb, sa ) );
				}
				else if ( ptr->Reason == IntersectionEventType_t.TouchPersists )
				{
					var ab = new Collision( sa, sb, c );
					OnIntersectionUpdate?.InvokeWithWarning( ab );
					a.Body.DispatchIntersectionUpdate( ab );
					b.Body.DispatchIntersectionUpdate( new Collision( sb, sa, c ) );
				}
				else if ( ptr->Reason == IntersectionEventType_t.TriggerBegin )
				{
					a.Body.DispatchTriggerBegin( sa, sb );
					b.Body.DispatchTriggerBegin( sb, sa );
				}
				else if ( ptr->Reason == IntersectionEventType_t.TriggerEnd )
				{
					a.Body.DispatchTriggerEnd( sa, sb );
					b.Body.DispatchTriggerEnd( sb, sa );
				}
			}
			catch ( System.Exception e )
			{
				Log.Error( e );
			}
		}
	}

	/// <summary>
	/// Access the world's current gravity.
	/// </summary>
	public override Vector3 Gravity
	{
		get => gravity;
		set
		{
			if ( gravity == value ) return;

			gravity = value;
			world.SetGravity( gravity );
		}
	}

	/// <summary>
	/// If true then bodies will be able to sleep after a period of inactivity
	/// </summary>
	public override bool SleepingEnabled
	{
		get => world.IsSleepingEnabled();
		set
		{
			if ( value ) world.EnableSleeping();
			else world.DisableSleeping();
		}
	}

	internal override float MaximumLinearSpeed
	{
		set => world.SetMaximumLinearSpeed( value );
	}

	/// <summary>
	/// Physics simulation mode. See <see cref="PhysicsSimulationMode"/> for explanation of each mode.
	/// </summary>
	public override PhysicsSimulationMode SimulationMode
	{
		get => world.GetSimulation();
		set => world.SetSimulation( value );
	}

	/// <summary>
	/// Temp function for creating model physics until entity system handles it
	/// </summary>
	public override PhysicsGroup SetupPhysicsFromModel( Model model, PhysicsMotionType motionType )
	{
		return native.CreateAggregateInstance( model.native, Transform.Zero, 0, motionType );
	}

	/// <summary>
	/// Temp function for creating model physics until entity system handles it
	/// </summary>
	public override PhysicsGroup SetupPhysicsFromModel( Model model, Transform transform, PhysicsMotionType motionType )
	{
		return native.CreateAggregateInstance( model.native, transform, 0, motionType );
	}

	internal override void UnregisterBody( PhysicsBodyInternal physicsBody )
	{
		world.RemoveBody( (PhysicsBody3d)physicsBody );
		base.UnregisterBody( physicsBody );
	}

}
