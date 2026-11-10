using System.Runtime.InteropServices;
using NativeEngine;

namespace Sandbox;

sealed partial class ScenePhysics2dSystem
{
	[StructLayout( LayoutKind.Sequential )]
	struct B2ContactBeginTouchEvent
	{
		public b2ShapeId shapeIdA;
		public b2ShapeId shapeIdB;
		public b2ContactId contactId;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct B2ContactEndTouchEvent
	{
		public b2ShapeId shapeIdA;
		public b2ShapeId shapeIdB;
		public b2ContactId contactId;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct B2ContactHitEvent
	{
		public b2ShapeId shapeIdA;
		public b2ShapeId shapeIdB;
		public b2ContactId contactId;
		public b2Vec2 point;
		public b2Vec2 normal;
		public float approachSpeed;
	}

	static PhysicsContact.Target? FindTarget( b2ShapeId shapeId )
	{
		if ( !Box2d.b2Shape_IsValid( shapeId ) ) return null;

		var shape = PhysicsShape2d.FromUserData( shapeId );
		if ( shape?.Collider is not { } collider ) return null;

		return new PhysicsContact.Target( collider.PhysicsBody, shape.Owner, collider.Surface ?? Surface.FindByIndex( 0 ) );
	}

	unsafe void ProcessContactEvents()
	{
		var world = World;
		var events = Box2d.b2World_GetContactEvents( world.WorldId );

		if ( events.beginCount > 0 )
		{
			var span = new ReadOnlySpan<B2ContactBeginTouchEvent>( (void*)events.beginEvents, events.beginCount );
			foreach ( ref readonly var e in span )
			{
				if ( FindTarget( e.shapeIdA ) is not { } a || FindTarget( e.shapeIdB ) is not { } b ) continue;

				var contact = GetContact( e.contactId );

				var sa = new CollisionSource( a );
				var sb = new CollisionSource( b );

				a.Body?.DispatchIntersectionStart( new Collision( sa, sb, contact ) );
				if ( !world.IsValid() ) return;

				b.Body?.DispatchIntersectionStart( new Collision( sb, sa, contact ) );
				if ( !world.IsValid() ) return;
			}
		}

		if ( events.endCount > 0 )
		{
			var span = new ReadOnlySpan<B2ContactEndTouchEvent>( (void*)events.endEvents, events.endCount );
			foreach ( ref readonly var e in span )
			{
				if ( FindTarget( e.shapeIdA ) is not { } a || FindTarget( e.shapeIdB ) is not { } b ) continue;

				var sa = new CollisionSource( a );
				var sb = new CollisionSource( b );

				a.Body?.DispatchIntersectionEnd( new CollisionStop( sa, sb ) );
				if ( !world.IsValid() ) return;
				b.Body?.DispatchIntersectionEnd( new CollisionStop( sb, sa ) );
				if ( !world.IsValid() ) return;
			}
		}

		if ( events.hitCount > 0 )
		{
			var span = new ReadOnlySpan<B2ContactHitEvent>( (void*)events.hitEvents, events.hitCount );
			foreach ( ref readonly var e in span )
			{
				if ( FindTarget( e.shapeIdA ) is not { } a || FindTarget( e.shapeIdB ) is not { } b ) continue;

				var contact = new PhysicsContact( e.point, e.normal, e.approachSpeed, 0f );

				var collision = new Collision( new CollisionSource( a ), new CollisionSource( b ), contact );
				foreach ( var ev in CollisionEvents )
				{
					ev.OnCollisionHit( collision );
					if ( !world.IsValid() ) return;
				}

				var collisionFlip = new Collision( new CollisionSource( b ), new CollisionSource( a ), contact );
				foreach ( var ev in CollisionEvents )
				{
					ev.OnCollisionHit( collisionFlip );
					if ( !world.IsValid() ) return;
				}
			}
		}

		while ( world.PreSolveEvents.TryDequeue( out var e ) )
		{
			if ( FindTarget( e.ShapeIdA ) is not { } a || FindTarget( e.ShapeIdB ) is not { } b ) continue;

			var contact = new PhysicsContact( e.Point, e.Normal, 0f, 0f );

			var sa = new CollisionSource( a );
			var sb = new CollisionSource( b );

			a.Body?.DispatchIntersectionUpdate( new Collision( sa, sb, contact ) );
			if ( !world.IsValid() ) return;

			b.Body?.DispatchIntersectionUpdate( new Collision( sb, sa, contact ) );
			if ( !world.IsValid() ) return;
		}
	}

	unsafe void ProcessSensorEvents()
	{
		var world = World;
		var events = Box2d.b2World_GetSensorEvents( world.WorldId );

		if ( events.beginCount > 0 )
		{
			var span = new ReadOnlySpan<b2SensorBeginTouchEvent>( (void*)events.beginEvents, events.beginCount );
			foreach ( ref readonly var e in span )
			{
				if ( FindTarget( e.sensorShapeId ) is not { } sensor || FindTarget( e.visitorShapeId ) is not { } visitor ) continue;

				var ss = new CollisionSource( sensor );
				var sv = new CollisionSource( visitor );

				sensor.Body?.DispatchTriggerBegin( ss, sv );
				if ( !world.IsValid() ) return;

				visitor.Body?.DispatchTriggerBegin( sv, ss );
				if ( !world.IsValid() ) return;
			}
		}

		if ( events.endCount > 0 )
		{
			var span = new ReadOnlySpan<b2SensorEndTouchEvent>( (void*)events.endEvents, events.endCount );
			foreach ( ref readonly var e in span )
			{
				if ( FindTarget( e.sensorShapeId ) is not { } sensor || FindTarget( e.visitorShapeId ) is not { } visitor ) continue;

				var ss = new CollisionSource( sensor );
				var sv = new CollisionSource( visitor );

				sensor.Body?.DispatchTriggerEnd( ss, sv );
				if ( !world.IsValid() ) return;

				visitor.Body?.DispatchTriggerEnd( sv, ss );
				if ( !world.IsValid() ) return;
			}
		}
	}

	static PhysicsContact GetContact( b2ContactId contactId )
	{
		if ( !Box2d.b2Contact_IsValid( contactId ) )
			return default;

		var data = Box2d.b2Contact_GetData( contactId );
		ref var m = ref data.manifold;

		if ( m.pointCount <= 0 )
			return default;

		float totalImpulse = 0f;
		float bestSeparation = float.MaxValue;
		var point = Vector3.Zero;
		float approachSpeed = 0f;

		for ( int i = 0; i < m.pointCount; i++ )
		{
			ref var mp = ref (i == 0 ? ref m.p0 : ref m.p1);
			totalImpulse += mp.normalImpulse;

			if ( mp.separation < bestSeparation )
			{
				bestSeparation = mp.separation;
				approachSpeed = -mp.normalVelocity;
				point = mp.clipPoint;
			}
		}

		Vector3 normal = m.normal;
		return new PhysicsContact( point, normal, approachSpeed, totalImpulse );
	}
}
