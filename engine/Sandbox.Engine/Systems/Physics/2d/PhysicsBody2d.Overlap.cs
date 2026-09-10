using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NativeEngine;

namespace Sandbox;

partial class PhysicsBody2d
{
	const int MaxStackContacts = 16;

	public override bool CheckOverlap( PhysicsBody body )
	{
		if ( !body.IsValid() || !this.IsValid() )
			return false;

		return CheckOverlap( body, body.Transform );
	}

	public override unsafe bool CheckOverlap( PhysicsBody body, Transform transform )
	{
		if ( !this.IsValid() || !body.IsValid() )
			return false;

		if ( body._body is not PhysicsBody2d other )
			return false;

		var bodyTransform = Box2d.b2Body_GetTransform( BodyId );
		var filter = new b2QueryFilter { categoryBits = ulong.MaxValue, maskBits = ulong.MaxValue };

		foreach ( var shape in _shapes )
		{
			var shapeType = Box2d.b2Shape_GetType( shape.ShapeId );
			b2ShapeProxy proxy = default;

			switch ( shapeType )
			{
				case b2ShapeType.b2_circleShape:
					{
						var circle = Box2d.b2Shape_GetCircle( shape.ShapeId );
						proxy.p0 = bodyTransform.TransformPoint( circle.center );
						proxy.count = 1;
						proxy.radius = circle.radius;
						break;
					}
				case b2ShapeType.b2_capsuleShape:
					{
						var capsule = Box2d.b2Shape_GetCapsule( shape.ShapeId );
						proxy.p0 = bodyTransform.TransformPoint( capsule.center1 );
						proxy.p1 = bodyTransform.TransformPoint( capsule.center2 );
						proxy.count = 2;
						proxy.radius = capsule.radius;
						break;
					}
				case b2ShapeType.b2_polygonShape:
					{
						var polygon = Box2d.b2Shape_GetPolygon( shape.ShapeId );
						proxy.p0 = bodyTransform.TransformPoint( polygon.v0 );
						proxy.p1 = bodyTransform.TransformPoint( polygon.v1 );
						proxy.p2 = bodyTransform.TransformPoint( polygon.v2 );
						proxy.p3 = bodyTransform.TransformPoint( polygon.v3 );
						proxy.p4 = bodyTransform.TransformPoint( polygon.v4 );
						proxy.p5 = bodyTransform.TransformPoint( polygon.v5 );
						proxy.p6 = bodyTransform.TransformPoint( polygon.v6 );
						proxy.p7 = bodyTransform.TransformPoint( polygon.v7 );
						proxy.count = polygon.count;
						proxy.radius = polygon.radius;
						break;
					}
				default:
					continue;
			}

			var ctx = new OverlapBodyContext { TargetBody = other.Owner, Found = false };
			var handle = GCHandle.Alloc( ctx );

			try
			{
				Box2d.b2World_OverlapShape(
					(World as PhysicsWorld2d).WorldId,
					(IntPtr)(&proxy),
					filter,
					OverlapBodyCallback,
					GCHandle.ToIntPtr( handle )
				);
			}
			finally
			{
				handle.Free();
			}

			if ( ctx.Found ) return true;
		}

		return false;
	}

	class OverlapBodyContext
	{
		public PhysicsBody TargetBody;
		public bool Found;
	}

	static readonly unsafe IntPtr OverlapBodyCallback = (IntPtr)(delegate* unmanaged[Cdecl]< b2ShapeId, IntPtr, byte >)&OnOverlapBodyResult;

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static byte OnOverlapBodyResult( b2ShapeId shapeId, IntPtr context )
	{
		try
		{
			var ctx = (OverlapBodyContext)GCHandle.FromIntPtr( context ).Target;

			var hitShape = PhysicsShape2d.FromUserData( shapeId );
			if ( hitShape?.Body == ctx.TargetBody )
			{
				ctx.Found = true;
				return 0;
			}

			return 1;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error in 2D overlap query: {e.Message}" );
			return 1;
		}
	}

	internal override unsafe bool IsTouching( PhysicsBody body, bool triggersOnly )
	{
		if ( !body.IsValid() || !this.IsValid() )
			return false;

		if ( body._body is not PhysicsBody2d other )
			return false;

		var capacity = Box2d.b2Body_GetContactCapacity( BodyId );
		if ( capacity <= 0 ) return false;

		b2ContactData[] rented = null;
		Span<b2ContactData> contacts = capacity <= MaxStackContacts
			? stackalloc b2ContactData[MaxStackContacts]
			: (rented = ArrayPool<b2ContactData>.Shared.Rent( capacity ));

		try
		{
			int count;
			fixed ( b2ContactData* ptr = contacts )
				count = Box2d.b2Body_GetContactData( BodyId, (IntPtr)ptr, capacity );

			for ( int i = 0; i < count; i++ )
			{
				ref var c = ref contacts[i];
				var shapeA = PhysicsShape2d.FromUserData( c.shapeIdA );
				var shapeB = PhysicsShape2d.FromUserData( c.shapeIdB );
				if ( shapeA is null || shapeB is null ) continue;

				bool involves = (shapeA.Body == Owner && shapeB.Body == other.Owner) ||
								(shapeA.Body == other.Owner && shapeB.Body == Owner);

				if ( !involves ) continue;

				if ( triggersOnly && !shapeA.IsTrigger && !shapeB.IsTrigger )
					continue;

				return true;
			}

			return false;
		}
		finally
		{
			if ( rented is not null )
				ArrayPool<b2ContactData>.Shared.Return( rented );
		}
	}

	internal override unsafe bool IsTouching( PhysicsShape shape, bool triggersOnly )
	{
		if ( !shape.IsValid() || !this.IsValid() )
			return false;

		if ( shape._shape is not PhysicsShape2d targetShape )
			return false;

		var capacity = Box2d.b2Body_GetContactCapacity( BodyId );
		if ( capacity <= 0 ) return false;

		b2ContactData[] rented = null;
		Span<b2ContactData> contacts = capacity <= MaxStackContacts
			? stackalloc b2ContactData[MaxStackContacts]
			: (rented = ArrayPool<b2ContactData>.Shared.Rent( capacity ));

		try
		{
			int count;
			fixed ( b2ContactData* ptr = contacts )
				count = Box2d.b2Body_GetContactData( BodyId, (IntPtr)ptr, capacity );

			for ( int i = 0; i < count; i++ )
			{
				ref var c = ref contacts[i];

				bool involves = c.shapeIdA.index1 == targetShape.ShapeId.index1 ||
								c.shapeIdB.index1 == targetShape.ShapeId.index1;

				if ( !involves ) continue;

				if ( triggersOnly )
				{
					var shapeA = PhysicsShape2d.FromUserData( c.shapeIdA );
					var shapeB = PhysicsShape2d.FromUserData( c.shapeIdB );
					if ( shapeA is not null && !shapeA.IsTrigger && shapeB is not null && !shapeB.IsTrigger )
						continue;
				}

				return true;
			}

			return false;
		}
		finally
		{
			if ( rented is not null )
				ArrayPool<b2ContactData>.Shared.Return( rented );
		}
	}
}
