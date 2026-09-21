using NativeEngine;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// 2D physics world backed by a Box2D b2WorldId.
/// </summary>
internal sealed partial class PhysicsWorld2d : PhysicsWorldInternal
{
	internal b2WorldId WorldId;
	GCHandle _handle;

	internal readonly ConcurrentQueue<PreSolveEvent> PreSolveEvents = new();

	internal struct PreSolveEvent
	{
		public b2ShapeId ShapeIdA;
		public b2ShapeId ShapeIdB;
		public b2Vec2 Point;
		public b2Vec2 Normal;
	}

	public override bool IsValid => Box2d.b2World_IsValid( WorldId );

	PhysicsBody _worldBody;
	public override PhysicsBody Body => _worldBody ??= CreateBody();

	static readonly unsafe nint PreSolveFcn = (nint)(delegate* unmanaged[Cdecl]< b2ShapeId, b2ShapeId, b2Vec2, b2Vec2, nint, byte >)&PreSolveCallback;
	static readonly unsafe nint CustomFilterFcn = (nint)(delegate* unmanaged[Cdecl]< b2ShapeId, b2ShapeId, nint, byte >)&CustomFilterCallback;

	public PhysicsWorld2d()
	{
		Box2d.b2SetLengthUnitsPerMeter( 40.0f );

		gravity = Vector2.Down * 850;

		var def = Box2d.b2DefaultWorldDef();
		def.gravity = gravity;
		def.enableContinuous = true;
		def.enableContactSoftening = true;
		def.contactHertz = 120f;
		def.contactDampingRatio = 10f;
		def.contactSpeed = 120f;
		def.hitEventThreshold = 40f;
		def.maximumLinearSpeed = 1000f * 40f;
		def.workerCount = Math.Clamp( Environment.ProcessorCount / 2, 1, 32 );
		WorldId = Box2d.b2CreateWorld( def );

		_handle = GCHandle.Alloc( this );
		Box2d.b2World_SetUserData( WorldId, GCHandle.ToIntPtr( _handle ) );
		Box2d.b2World_SetPreSolveCallback( WorldId, PreSolveFcn, GCHandle.ToIntPtr( _handle ) );
		Box2d.b2World_SetCustomFilterCallback( WorldId, CustomFilterFcn, GCHandle.ToIntPtr( _handle ) );

		All.Add( this );
	}

	internal static PhysicsWorld2d FromUserData( b2WorldId worldId )
	{
		if ( !Box2d.b2World_IsValid( worldId ) )
			return null;

		var ptr = Box2d.b2World_GetUserData( worldId );
		if ( ptr == IntPtr.Zero ) return null;
		return GCHandle.FromIntPtr( ptr ).Target as PhysicsWorld2d;
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static byte PreSolveCallback( b2ShapeId shapeIdA, b2ShapeId shapeIdB, b2Vec2 point, b2Vec2 normal, nint context )
	{
		try
		{
			if ( context == nint.Zero ) return 1;

			var world = GCHandle.FromIntPtr( context ).Target as PhysicsWorld2d;
			if ( world is null ) return 1;

			world.PreSolveEvents.Enqueue( new PreSolveEvent
			{
				ShapeIdA = shapeIdA,
				ShapeIdB = shapeIdB,
				Point = point,
				Normal = normal,
			} );

			return 1;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error in 2D pre-solve: {e.Message}" );
			return 1;
		}
	}

	/// <summary>
	/// Box2D asks whether a pair may collide before creating the contact. Returning 0 stops the contact.
	/// </summary>
	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static byte CustomFilterCallback( b2ShapeId shapeIdA, b2ShapeId shapeIdB, nint context )
	{
		try
		{
			if ( context == nint.Zero ) return 1;

			var world = GCHandle.FromIntPtr( context ).Target as PhysicsWorld2d;
			if ( world is null ) return 1;

			var shapeA = PhysicsShape2d.FromUserData( shapeIdA );
			var shapeB = PhysicsShape2d.FromUserData( shapeIdB );

			if ( shapeA is null || shapeB is null ) return 1;

			if ( !shapeA.IsTrigger && !shapeB.IsTrigger && (!shapeA.EnableSolidCollisions || !shapeB.EnableSolidCollisions) )
				return 0;

			return world.ShouldCollide( shapeA, shapeB ) ? (byte)1 : (byte)0;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error in 2D collision filter: {e.Message}" );
			return 1;
		}
	}

	/// <summary>
	/// Evaluate the project collision rules for a pair of shapes. Anything but <see cref="CollisionRules.Result.Collide"/>
	/// means the pair doesn't collide.
	/// </summary>
	bool ShouldCollide( PhysicsShape2d shapeA, PhysicsShape2d shapeB )
	{
		var rules = CollisionRules;
		if ( rules is null ) return true;

		// Shape tags are a snapshot kept up to date on the main thread, GameObject.Tags is a lazy
		// cache that mutates when read - this runs on Box2D worker threads.
		var tagsA = shapeA.Tags.GetTokens();
		var tagsB = shapeB.Tags.GetTokens();
		if ( tagsA.Count == 0 && tagsB.Count == 0 ) return true;

		return rules.GetCollisionRule( tagsA, tagsB ) == CollisionRules.Result.Collide;
	}

	internal override PhysicsBodyInternal CreateBodyInternal()
	{
		var def = Box2d.b2DefaultBodyDef();
		var bodyId = Box2d.b2CreateBody( WorldId, def );
		var body = new PhysicsBody2d( this, bodyId );
		RegisterBody( body );
		return body;
	}

	public override void Delete()
	{
		if ( Box2d.b2World_IsValid( WorldId ) )
		{
			foreach ( var body in Bodies.ToArray() )
				body.Remove();

			Box2d.b2DestroyWorld( WorldId );
			WorldId = default;
		}

		if ( _handle.IsAllocated )
			_handle.Free();

		All.Remove( this );
	}

	const int MinSubSteps = 4;

	public override unsafe void Step( double worldTime, float delta, int subSteps )
	{
		if ( !Box2d.b2World_IsValid( WorldId ) ) return;

		base.Step( worldTime, delta, subSteps );

		Box2d.b2World_Step( WorldId, delta, Math.Max( subSteps, MinSubSteps ) );

		ProcessJointBreakEvents();
	}

	unsafe void ProcessJointBreakEvents()
	{
		var events = Box2d.b2World_GetJointEvents( WorldId );
		if ( events.count <= 0 ) return;

		var span = new ReadOnlySpan<b2JointEvent>( (void*)events.jointEvents, events.count );
		foreach ( ref readonly var e in span )
		{
			var joint = PhysicsJoint2d.FromUserData( e.jointId );
			joint?.InternalJointBroken();
		}
	}

	public override Vector3 Gravity
	{
		get => gravity;
		set
		{
			if ( gravity == value ) return;

			gravity = value;

			if ( Box2d.b2World_IsValid( WorldId ) )
			{
				Box2d.b2World_SetGravity( WorldId, gravity );
			}
		}
	}

	public override bool SleepingEnabled
	{
		get => Box2d.b2World_IsValid( WorldId ) && Box2d.b2World_IsSleepingEnabled( WorldId );
		set
		{
			if ( !Box2d.b2World_IsValid( WorldId ) ) return;
			Box2d.b2World_EnableSleeping( WorldId, value );
		}
	}
}
