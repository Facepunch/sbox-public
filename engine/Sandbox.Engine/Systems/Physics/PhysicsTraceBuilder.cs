
using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox;

public partial struct PhysicsTraceBuilder
{
	internal PhysicsWorld targetWorld;
	internal PhysicsBody targetBody;
	internal PhysicsTrace.Request request;

	/// <summary>
	/// Do not expose! We want to force this whole thing into as tight of a box as possible!
	/// </summary>
	internal Func<PhysicsShape, bool> filterCallback;

	internal PhysicsTraceBuilder( PhysicsWorld world )
	{
		targetWorld = world;
		request = default;
		request.StartShape.Type = PhysicsTrace.Request.ShapeType.Sphere;
		request.StartShape.StartRot = Rotation.Identity;
	}

	/// <summary>
	/// Casts a sphere from point A to point B.
	/// </summary>
	public PhysicsTraceBuilder Sphere( float radius, in Vector3 from, in Vector3 to ) => Ray( from, to ).Radius( radius );

	/// <summary>
	/// Casts a sphere from a given position and direction, up to a given distance.
	/// </summary>
	public PhysicsTraceBuilder Sphere( float radius, in Ray ray, in float distance ) => Ray( ray, distance ).Radius( radius );

	/// <summary>
	/// Casts a box from point A to point B.
	/// </summary>
	public PhysicsTraceBuilder Box( Vector3 extents, in Vector3 from, in Vector3 to )
	{
		return Ray( from, to ).Size( extents );
	}

	/// <summary>
	/// Casts a box from a given position and direction, up to a given distance.
	/// </summary>
	public PhysicsTraceBuilder Box( Vector3 extents, in Ray ray, in float distance )
	{
		return Ray( ray, distance ).Size( extents );
	}

	/// <summary>
	/// Casts a box from point A to point B.
	/// </summary>
	public PhysicsTraceBuilder Box( BBox bbox, in Vector3 from, in Vector3 to )
	{
		return Ray( from, to ).Size( bbox );
	}

	/// <summary>
	/// Casts a box from a given position and direction, up to a given distance.
	/// </summary>
	public PhysicsTraceBuilder Box( BBox bbox, in Ray ray, in float distance )
	{
		return Ray( ray, distance ).Size( bbox );
	}

	/// <summary>
	/// Casts a capsule
	/// </summary>
	public readonly PhysicsTraceBuilder Capsule( Capsule capsule )
	{
		var t = this;
		t.request.StartShape.Type = PhysicsTrace.Request.ShapeType.Capsule;
		t.request.StartShape.Mins = capsule.CenterA;
		t.request.StartShape.Maxs = capsule.CenterB;
		t.request.StartShape.Radius = capsule.Radius;
		return t;
	}

	/// <summary>
	/// Casts a capsule from point A to point B.
	/// </summary>
	public PhysicsTraceBuilder Capsule( Capsule capsule, in Vector3 from, in Vector3 to )
	{
		request.StartPos = from;
		request.EndPos = to;
		request.StartShape.Type = PhysicsTrace.Request.ShapeType.Capsule;
		request.StartShape.Mins = capsule.CenterA;
		request.StartShape.Maxs = capsule.CenterB;
		request.StartShape.Radius = capsule.Radius;
		return this;
	}

	/// <summary>
	/// Casts a capsule from a given position and direction, up to a given distance.
	/// </summary>
	public PhysicsTraceBuilder Capsule( Capsule capsule, in Ray ray, in float distance )
	{
		request.StartPos = ray.Position;
		request.EndPos = ray.ProjectSafe( distance );
		request.StartShape.Type = PhysicsTrace.Request.ShapeType.Capsule;
		request.StartShape.Mins = capsule.CenterA;
		request.StartShape.Maxs = capsule.CenterB;
		request.StartShape.Radius = capsule.Radius;
		return this;
	}

	/// <summary>
	/// Casts a cylinder
	/// </summary>
	public readonly PhysicsTraceBuilder Cylinder( float height, float radius )
	{
		var t = this;
		var halfHeight = height * 0.5f;
		t.request.StartShape.Type = PhysicsTrace.Request.ShapeType.Cylinder;
		t.request.StartShape.Mins = Vector3.Down * halfHeight;
		t.request.StartShape.Maxs = Vector3.Up * halfHeight;
		t.request.StartShape.Radius = radius;
		return t;
	}

	/// <summary>
	/// Casts a cylinder from point A to point B.
	/// </summary>
	public PhysicsTraceBuilder Cylinder( float height, float radius, in Vector3 from, in Vector3 to )
	{
		var halfHeight = height * 0.5f;
		request.StartPos = from;
		request.EndPos = to;
		request.StartShape.Type = PhysicsTrace.Request.ShapeType.Cylinder;
		request.StartShape.Mins = Vector3.Down * halfHeight;
		request.StartShape.Maxs = Vector3.Up * halfHeight;
		request.StartShape.Radius = radius;
		return this;
	}

	/// <summary>
	/// Casts a cylinder from a given position and direction, up to a given distance.
	/// </summary>
	public PhysicsTraceBuilder Cylinder( float height, float radius, in Ray ray, in float distance )
	{
		var halfHeight = height * 0.5f;
		request.StartPos = ray.Position;
		request.EndPos = ray.ProjectSafe( distance );
		request.StartShape.Type = PhysicsTrace.Request.ShapeType.Cylinder;
		request.StartShape.Mins = Vector3.Down * halfHeight;
		request.StartShape.Maxs = Vector3.Up * halfHeight;
		request.StartShape.Radius = radius;
		return this;
	}

	/// <summary>
	/// Casts a ray from point A to point B.
	/// </summary>
	public PhysicsTraceBuilder Ray( in Vector3 from, in Vector3 to )
	{
		request.StartShape.Type = PhysicsTrace.Request.ShapeType.Sphere;
		request.StartShape.Radius = 0.0f;
		request.StartPos = from;
		request.EndPos = to;
		return this;
	}

	/// <summary>
	/// Casts a ray from a given position and direction, up to a given distance.
	/// </summary>
	public PhysicsTraceBuilder Ray( in Ray ray, in float distance )
	{
		request.StartShape.Type = PhysicsTrace.Request.ShapeType.Sphere;
		request.StartShape.Radius = 0.0f;
		request.StartPos = ray.Position;
		request.EndPos = ray.ProjectSafe( distance );
		return this;
	}

	/// <summary>
	/// Casts a PhysicsBody from its current position and rotation to desired end point.
	/// </summary>
	public PhysicsTraceBuilder Body( PhysicsBody body, in Vector3 to )
	{
		targetBody = body;
		request.StartPos = body.Position;
		request.EndPos = to;
		request.StartShape.StartRot = body.Rotation;
		return this;
	}

	/// <summary>
	/// Casts a PhysicsBody from a position and rotation to desired end point.
	/// </summary>
	public PhysicsTraceBuilder Body( PhysicsBody body, in Transform from, in Vector3 to )
	{
		targetBody = body;
		request.StartPos = from.Position;
		request.EndPos = to;
		request.StartShape.StartRot = from.Rotation;
		return this;
	}

	/// <summary>
	/// Sweeps each <see cref="PhysicsShape">PhysicsShape</see> of given PhysicsBody and returns the closest collision. Does not support Mesh PhysicsShapes.
	/// Basically 'hull traces' but with physics shapes.
	/// Same as tracing a body but allows rotation to change during the sweep.
	/// </summary>
	public PhysicsTraceBuilder Sweep( in PhysicsBody body, in Transform from, in Transform to )
	{
		targetBody = body;
		request.StartPos = from.Position;
		request.EndPos = to.Position;
		request.StartShape.StartRot = from.Rotation;
		return this;
	}

	/// <summary>
	/// Creates a Trace.Sweep using the <see cref="PhysicsBody">PhysicsBody</see>'s position as the starting position.
	/// </summary>
	public PhysicsTraceBuilder Sweep( in PhysicsBody body, in Transform to )
	{
		return Sweep( body, body.Transform, to );
	}

	/// <summary>
	/// Sets the start and end positions of the trace request
	/// </summary>
	public readonly PhysicsTraceBuilder FromTo( in Vector3 from, in Vector3 to )
	{
		var t = this;
		t.request.StartPos = from;
		t.request.EndPos = to;
		return t;
	}

	/// <summary>
	/// Sets the start transform and end position of the trace request
	/// </summary>
	public readonly PhysicsTraceBuilder FromTo( in Transform from, in Vector3 to )
	{
		var t = this;
		t.request.StartPos = from.Position;
		t.request.StartShape.StartRot = from.Rotation;
		t.request.EndPos = to;
		return t;
	}

	/// <summary>
	/// Sets the start rotation of the trace request
	/// </summary>
	public readonly PhysicsTraceBuilder Rotated( in Rotation rotation )
	{
		var t = this;
		t.request.StartShape.StartRot = rotation;
		return t;
	}

	/// <summary>
	/// Include triggers in the trace
	/// </summary>
	public readonly PhysicsTraceBuilder HitTriggers()
	{
		var t = this;
		t.request.TriggerFilter = 1;
		return t;
	}

	/// <summary>
	/// Only hit triggers
	/// </summary>
	public readonly PhysicsTraceBuilder HitTriggersOnly()
	{
		var t = this;
		t.request.TriggerFilter = 2;
		return t;
	}

	/// <summary>
	/// Ignore static objects in the trace
	/// </summary>
	public readonly PhysicsTraceBuilder IgnoreStatic()
	{
		var t = this;
		t.request.ObjectSetMask |= 1 << 0;
		return t;
	}

	/// <summary>
	/// Ignore dynamic objects in the trace
	/// </summary>
	public readonly PhysicsTraceBuilder IgnoreDynamic()
	{
		var t = this;
		t.request.ObjectSetMask |= 1 << 1;
		return t;
	}

	/// <summary>
	/// Ignore keyframed objects in the trace
	/// </summary>
	public readonly PhysicsTraceBuilder IgnoreKeyframed()
	{
		var t = this;
		t.request.ObjectSetMask |= 1 << 2;
		return t;
	}

	/// <summary>
	/// Compute hit position.
	/// </summary>
	public readonly PhysicsTraceBuilder UseHitPosition( bool enabled = true )
	{
		var t = this;
		t.request.UseHitPosition = (byte)(enabled ? 1 : 0);
		return t;
	}

	/// <summary>
	/// Makes this trace an axis aligned box of given size. Extracts mins and maxs from the Bounding Box.
	/// </summary>
	public readonly PhysicsTraceBuilder Size( in BBox hull )
	{
		return Size( hull.Mins, hull.Maxs );
	}

	/// <summary>
	/// Makes this trace an axis aligned box of given size. Calculates mins and maxs by assuming given size is (maxs-mins) and the center is in the middle.
	/// </summary>
	public readonly PhysicsTraceBuilder Size( in Vector3 size )
	{
		return Size( size * -0.5f, size * 0.5f );
	}

	/// <summary>
	/// Makes this trace an axis aligned box of given size.
	/// </summary>
	public readonly PhysicsTraceBuilder Size( in Vector3 mins, in Vector3 maxs )
	{
		// Assert - make sure these are in the right order?
		var c = this;
		c.request.StartShape.Type = PhysicsTrace.Request.ShapeType.Box;
		c.request.StartShape.Mins = mins;
		c.request.StartShape.Maxs = maxs;
		return c;
	}

	// Named this radius instead of size just incase there's some casting going on and Size gets called instead
	/// <summary>
	/// Makes this trace a sphere of given radius.
	/// </summary>
	public readonly PhysicsTraceBuilder Radius( float radius )
	{
		var c = this;
		c.request.StartShape.Type = PhysicsTrace.Request.ShapeType.Sphere;
		c.request.StartShape.Radius = radius;
		return c;
	}

	[ThreadStatic]
	static Func<PhysicsShape, bool> _currentfilterCallback;

	[UnmanagedCallersOnly]
	static byte FilterFunctionInternal( int value )
	{
		try
		{
			Assert.NotNull( _currentfilterCallback );

			var shape = HandleIndex.Get<PhysicsShape>( value );
			if ( shape is null ) return 1; // should never happen, just use default behaviour

			if ( _currentfilterCallback( shape ) ) return 1;
			return 0;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error in trace filter: {e.Message}" );
			return 1;
		}
	}

	readonly unsafe PhysicsTraceResult[] GetResults()
	{
		if ( targetWorld is null )
			throw new InvalidOperationException( "No physics world to trace" );

		if ( targetBody is not null && !targetBody.IsValid() )
		{
			throw new InvalidOperationException( "The physics body has been released" );
		}

		var r = request;

		r.World = (IntPtr)targetWorld.native;

		if ( targetBody.IsValid() )
		{
			r.Body = (IntPtr)targetBody.native;
		}

		if ( filterCallback is not null )
		{
			r.FilterDelegate = (IntPtr)((delegate* unmanaged< int, byte >)&FilterFunctionInternal);
			_currentfilterCallback = filterCallback;
		}

		var nativeResults = CUtlVectorTraceResult.Create( 32, 32 );
		PhysicsTrace.TraceAll( r, nativeResults );
		var count = nativeResults.Count();

		_currentfilterCallback = default;

		var results = new PhysicsTraceResult[count];

		for ( var i = 0; i < count; i++ )
		{
			results[i] = PhysicsTraceResult.From( nativeResults.Element( i ), request.StartShape );
		}

		nativeResults.DeleteThis();

		return results;
	}

	unsafe readonly PhysicsTraceResult GetResult()
	{
		if ( targetWorld is null )
			throw new InvalidOperationException( "No physics world to trace" );

		if ( targetBody is not null && !targetBody.IsValid() )
		{
			throw new InvalidOperationException( "The physics body has been released" );
		}

		var r = request;
		r.World = (IntPtr)targetWorld.native;

		if ( targetBody.IsValid() )
		{
			r.Body = (IntPtr)targetBody.native;
		}

		if ( filterCallback is not null )
		{
			r.FilterDelegate = (IntPtr)((delegate* unmanaged< int, byte >)&FilterFunctionInternal);
			_currentfilterCallback = filterCallback;
		}

		try
		{
			return PhysicsTraceResult.From( PhysicsTrace.Trace( r ), r.StartShape );
		}
		finally
		{
			_currentfilterCallback = default;
		}
	}

	/// <summary>
	/// Run the trace and return the result. The result will return the first hit.
	/// </summary>
	public readonly PhysicsTraceResult Run()
	{
		return GetResult();
	}

	/// <summary>
	/// Run the trace and return all hits as a result.
	/// </summary>
	public readonly PhysicsTraceResult[] RunAll()
	{
		return GetResults();
	}

	/// <summary>
	/// Traces only against the given capsule at the specified transform.
	/// </summary>
	/// <param name="capsule">The capsule to test against.</param>
	/// <param name="transform">Transform applied to the capsule.</param>
	/// <returns>The trace result.</returns>
	public readonly PhysicsTraceResult RunAgainstCapsule( in Capsule capsule, in Transform transform )
	{
		return PhysicsTraceResult.From( PhysicsTrace.TraceAgainstCapsule( request, capsule, transform ), request.StartShape );
	}

	/// <summary>
	/// Traces only against the given sphere at the specified transform.
	/// </summary>
	/// <param name="sphere">The sphere to test against.</param>
	/// <param name="transform">Transform applied to the sphere.</param>
	/// <returns>The trace result.</returns>
	public readonly PhysicsTraceResult RunAgainstSphere( in Sphere sphere, in Transform transform )
	{
		return PhysicsTraceResult.From( PhysicsTrace.TraceAgainstSphere( request, sphere, transform ), request.StartShape );
	}

	/// <summary>
	/// Traces only against the given bounding box at the specified transform.
	/// </summary>
	/// <param name="box">The bounding box to test against.</param>
	/// <param name="transform">Transform applied to the box.</param>
	/// <returns>The trace result.</returns>
	public readonly PhysicsTraceResult RunAgainstBBox( in BBox box, in Transform transform )
	{
		return PhysicsTraceResult.From( PhysicsTrace.TraceAgainstBBox( request, box, transform ), request.StartShape );
	}
}
