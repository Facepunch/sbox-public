using NativeEngine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox;

internal sealed partial class PhysicsWorld2d
{
	static readonly unsafe IntPtr OverlapCallback = (IntPtr)(delegate* unmanaged[Cdecl]< b2ShapeId, IntPtr, byte >)&OnOverlapResult;
	static readonly unsafe IntPtr CastCallback = (IntPtr)(delegate* unmanaged[Cdecl]< b2ShapeId, b2Vec2, b2Vec2, float, IntPtr, float >)&OnCastResult;
	// Native 3D physics shapes carry this tag even when their GameObject has no tags.
	static readonly uint SolidTag = StringToken.FindOrCreate( "solid" );

	class CastContext
	{
		public PhysicsTrace.Request Request;
		public Func<PhysicsShape, bool> FilterCallback;
		public List<PhysicsTraceResult> AllResults;
		public PhysicsTraceResult BestResult;
		public bool CollectAll;
	}

	internal override unsafe PhysicsTraceResult TraceSingle( in PhysicsTrace.Request request, PhysicsBody targetBody, Func<PhysicsShape, bool> filterCallback )
	{
		if ( !IsValid ) return base.TraceSingle( in request, targetBody, filterCallback );

		var ctx = new CastContext
		{
			Request = request,
			FilterCallback = filterCallback,
			BestResult = base.TraceSingle( in request, targetBody, filterCallback )
		};

		var handle = GCHandle.Alloc( ctx );
		try
		{
			Cast( in request, handle );
		}
		finally
		{
			handle.Free();
		}

		return ctx.BestResult;
	}

	internal override PhysicsTraceResult[] TraceMultiple( in PhysicsTrace.Request request, PhysicsBody targetBody, Func<PhysicsShape, bool> filterCallback )
	{
		var results = new List<PhysicsTraceResult>();
		TraceMultiple( in request, targetBody, filterCallback, results );
		return [.. results];
	}

	internal override unsafe int TraceMultiple( in PhysicsTrace.Request request, PhysicsBody targetBody, Func<PhysicsShape, bool> filterCallback, List<PhysicsTraceResult> results )
	{
		if ( !IsValid ) return 0;

		var ctx = new CastContext
		{
			Request = request,
			FilterCallback = filterCallback,
			AllResults = results,
			CollectAll = true
		};

		var startCount = results.Count;

		var handle = GCHandle.Alloc( ctx );
		try
		{
			Cast( in request, handle );
		}
		finally
		{
			handle.Free();
		}

		return results.Count - startCount;
	}

	unsafe void Cast( in PhysicsTrace.Request request, GCHandle handle )
	{
		var start = request.StartPos;
		var end = request.EndPos;
		var delta = end - start;

		b2Vec2 translation = delta;
		var filter = new b2QueryFilter { categoryBits = ulong.MaxValue, maskBits = ulong.MaxValue };
		var context = GCHandle.ToIntPtr( handle );

		if ( TryBuildProxy( in request, start, out var proxy ) )
		{
			Box2d.b2World_CastShape( WorldId, (IntPtr)(&proxy), translation, filter, CastCallback, context );
		}
		else
		{
			if ( delta.LengthSquared < float.Epsilon ) return;

			Box2d.b2World_CastRay( WorldId, start, translation, filter, CastCallback, context );
		}
	}

	static bool TryBuildProxy( in PhysicsTrace.Request req, Vector3 start, out b2ShapeProxy proxy )
	{
		proxy = default;
		var rot = req.StartShape.StartRot;

		switch ( req.StartShape.Type )
		{
			case PhysicsTrace.Request.ShapeType.Sphere:
				{
					float radius = req.StartShape.Radius.x;
					if ( radius <= 0f ) return false;

					proxy.p0 = start;
					proxy.count = 1;
					proxy.radius = radius;
					return true;
				}

			case PhysicsTrace.Request.ShapeType.Capsule:
				{
					proxy.p0 = start + rot * req.StartShape.Mins;
					proxy.p1 = start + rot * req.StartShape.Maxs;
					proxy.count = 2;
					proxy.radius = req.StartShape.Radius.x;
					return true;
				}

			case PhysicsTrace.Request.ShapeType.Box:
				{
					var mins = req.StartShape.Mins;
					var maxs = req.StartShape.Maxs;

					proxy.p0 = start + rot * new Vector3( mins.x, mins.y, 0f );
					proxy.p1 = start + rot * new Vector3( maxs.x, mins.y, 0f );
					proxy.p2 = start + rot * new Vector3( maxs.x, maxs.y, 0f );
					proxy.p3 = start + rot * new Vector3( mins.x, maxs.y, 0f );
					proxy.count = 4;
					proxy.radius = 0f;
					return true;
				}
		}

		return false;
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static float OnCastResult( b2ShapeId shapeId, b2Vec2 point, b2Vec2 normal, float fraction, IntPtr context )
	{
		try
		{
			var ctx = (CastContext)GCHandle.FromIntPtr( context ).Target;

			var shape = PhysicsShape2d.FromUserData( shapeId );
			if ( shape is null ) return 1f;
			if ( !PassesCastFilter( shape, in ctx.Request, ctx.FilterCallback ) ) return 1f;

			ref var req = ref ctx.Request;
			var startedSolid = fraction == 0f && normal.x == 0f && normal.y == 0f;
			var result = new PhysicsTraceResult
			{
				Hit = true,
				StartPosition = req.StartPos,
				EndPosition = Vector3.Lerp( req.StartPos, req.EndPos, fraction ),
				HitPosition = point,
				Normal = normal,
				StartedSolid = startedSolid,
				Fraction = fraction,
				Direction = (req.EndPos - req.StartPos).Normal,
				Body = shape.Body,
				Shape = shape.Owner,
				Surface = shape.Collider?.Surface ?? Surface.FindByIndex( 0 ),
				StartShape = req.StartShape
			};

			if ( ctx.CollectAll )
			{
				ctx.AllResults.Add( result );
				return 1f;
			}

			if ( fraction < ctx.BestResult.Fraction )
				ctx.BestResult = result;

			return fraction;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error in 2D trace filter: {e.Message}" );
			return 1f;
		}
	}

	static bool PassesCastFilter( PhysicsShape2d shape, in PhysicsTrace.Request request, Func<PhysicsShape, bool> filterCallback )
	{
		if ( filterCallback is not null && !filterCallback( shape.Owner ) )
			return false;

		if ( shape.Collider is { } collider && collider.ColliderFlags.Contains( ColliderFlags.IgnoreTraces ) )
			return false;

		var isTrigger = shape.IsTrigger;

		if ( request.TriggerFilter switch
		{
			0 => isTrigger,
			2 => !isTrigger,
			_ => false
		} )
			return false;

		if ( request.ObjectSetMask != 0 && shape.Body?._body is PhysicsBody2d { IsValid: true } body )
		{
			bool ignored = Box2d.b2Body_GetType( body.BodyId ) switch
			{
				b2BodyType.b2_staticBody => (request.ObjectSetMask & (1 << 0)) != 0,
				b2BodyType.b2_dynamicBody => (request.ObjectSetMask & (1 << 1)) != 0,
				b2BodyType.b2_kinematicBody => (request.ObjectSetMask & (1 << 2)) != 0,
				_ => false
			};

			if ( ignored ) return false;
		}

		var go = shape.Collider?.GameObject ?? shape.Body?.GameObject;
		if ( go is null ) return false;

		return PassesTagFilter( go, in request );
	}

	static unsafe bool PassesTagFilter( GameObject go, in PhysicsTrace.Request req )
	{
		bool hasRequire = false;
		bool hasAny = false;

		for ( int i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
		{
			if ( req.TagRequire[i] != 0 ) hasRequire = true;
			if ( req.TagAny[i] != 0 ) hasAny = true;
		}

		if ( !hasRequire && !hasAny && req.TagExclude[0] == 0 )
			return true;

		var tokens = go.Tags.GetTokens();
		bool HasTag( uint tag ) => tag == SolidTag || tokens.Contains( tag );

		for ( int i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
		{
			uint exclude = req.TagExclude[i];
			if ( exclude == 0 ) break;
			if ( HasTag( exclude ) ) return false;
		}

		if ( hasRequire )
		{
			for ( int i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
			{
				uint require = req.TagRequire[i];
				if ( require == 0 ) break;
				if ( !HasTag( require ) ) return false;
			}
		}

		if ( hasAny )
		{
			bool matched = false;
			for ( int i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
			{
				uint any = req.TagAny[i];
				if ( any == 0 ) break;
				if ( HasTag( any ) ) { matched = true; break; }
			}
			if ( !matched ) return false;
		}

		return true;
	}

	public override unsafe IEnumerable<GameObject> FindInPhysics( Sphere sphere )
	{
		var results = new HashSet<GameObject>();
		if ( !IsValid ) return results;

		var handle = GCHandle.Alloc( results );
		var filter = new b2QueryFilter { categoryBits = ulong.MaxValue, maskBits = ulong.MaxValue };

		var proxy = new b2ShapeProxy
		{
			p0 = sphere.Center,
			count = 1,
			radius = sphere.Radius
		};

		try
		{
			Box2d.b2World_OverlapShape( WorldId, (IntPtr)(&proxy), filter, OverlapCallback, GCHandle.ToIntPtr( handle ) );
		}
		finally
		{
			handle.Free();
		}

		return results;
	}

	public override unsafe IEnumerable<GameObject> FindInPhysics( BBox box )
	{
		var results = new HashSet<GameObject>();
		if ( !IsValid ) return results;

		var handle = GCHandle.Alloc( results );
		var filter = new b2QueryFilter { categoryBits = ulong.MaxValue, maskBits = ulong.MaxValue };

		var aabb = new b2AABB
		{
			lowerBound = box.Mins,
			upperBound = box.Maxs
		};

		try
		{
			Box2d.b2World_OverlapAABB( WorldId, aabb, filter, OverlapCallback, GCHandle.ToIntPtr( handle ) );
		}
		finally
		{
			handle.Free();
		}

		return results;
	}

	[ThreadStatic] static HashSet<PhysicsBody> _overlapBodies;
	static readonly unsafe IntPtr OverlapBodyCallback = (IntPtr)(delegate* unmanaged[Cdecl]< b2ShapeId, IntPtr, byte >)&OnOverlapBodyResult;

	public override unsafe int FindBodiesInPhysics( Vector3 center, float radius, Span<PhysicsBody> result )
	{
		if ( !IsValid || result.Length == 0 ) return 0;

		var bodies = _overlapBodies ??= new HashSet<PhysicsBody>();
		bodies.Clear();

		var handle = GCHandle.Alloc( bodies );
		var filter = new b2QueryFilter { categoryBits = ulong.MaxValue, maskBits = ulong.MaxValue };
		var proxy = new b2ShapeProxy { p0 = center, count = 1, radius = radius };

		try
		{
			Box2d.b2World_OverlapShape( WorldId, (IntPtr)(&proxy), filter, OverlapBodyCallback, GCHandle.ToIntPtr( handle ) );
		}
		finally
		{
			handle.Free();
		}

		int written = 0;
		foreach ( var body in bodies )
		{
			if ( written >= result.Length ) break;
			result[written++] = body;
		}
		return written;
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static byte OnOverlapBodyResult( b2ShapeId shapeId, IntPtr context )
	{
		try
		{
			var bodies = (HashSet<PhysicsBody>)GCHandle.FromIntPtr( context ).Target;

			var body = PhysicsShape2d.FromUserData( shapeId )?.Body;
			if ( body.IsValid() ) bodies.Add( body );

			return 1;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error in 2D overlap query: {e.Message}" );
			return 1;
		}
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static byte OnOverlapResult( b2ShapeId shapeId, IntPtr context )
	{
		try
		{
			var results = (HashSet<GameObject>)GCHandle.FromIntPtr( context ).Target;

			var shape = PhysicsShape2d.FromUserData( shapeId );
			var go = shape?.Collider?.GameObject ?? shape?.Body?.GameObject;
			if ( go is not null ) results.Add( go );

			return 1;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error in 2D overlap query: {e.Message}" );
			return 1;
		}
	}
}
