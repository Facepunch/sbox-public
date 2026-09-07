using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox;

partial class PhysicsWorld3d
{
	[ThreadStatic]
	static Func<PhysicsShape, bool> _currentFilterCallback;

	[UnmanagedCallersOnly]
	static byte FilterFunctionInternal( int value )
	{
		try
		{
			Assert.NotNull( _currentFilterCallback );

			var shape = HandleIndex.Get<PhysicsShape3d>( value );
			if ( shape is null ) return 1;

			if ( _currentFilterCallback( shape.Owner ) ) return 1;
			return 0;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error in trace filter: {e.Message}" );
			return 1;
		}
	}

	internal override unsafe PhysicsTraceResult TraceSingle( in PhysicsTrace.Request request, PhysicsBody targetBody, Func<PhysicsShape, bool> filterCallback )
	{
		if ( targetBody is not null && !targetBody.IsValid() )
			throw new InvalidOperationException( "The physics body has been released" );

		var r = request;
		r.World = native;

		if ( targetBody.IsValid() )
			r.Body = ((PhysicsBody3d)targetBody._body).native;

		if ( filterCallback is not null )
		{
			r.FilterDelegate = (IntPtr)((delegate* unmanaged< int, byte >)&FilterFunctionInternal);
			_currentFilterCallback = filterCallback;
		}

		try
		{
			return PhysicsTraceResult.From( PhysicsTrace.Trace( r ), r.StartShape );
		}
		finally
		{
			_currentFilterCallback = default;
		}
	}

	internal override unsafe PhysicsTraceResult[] TraceMultiple( in PhysicsTrace.Request request, PhysicsBody targetBody, Func<PhysicsShape, bool> filterCallback )
	{
		var results = new List<PhysicsTraceResult>();
		TraceMultiple( in request, targetBody, filterCallback, results );
		return [.. results];
	}

	internal override unsafe int TraceMultiple( in PhysicsTrace.Request request, PhysicsBody targetBody, Func<PhysicsShape, bool> filterCallback, List<PhysicsTraceResult> results )
	{
		if ( targetBody is not null && !targetBody.IsValid() )
			throw new InvalidOperationException( "The physics body has been released" );

		var r = request;
		r.World = native;

		if ( targetBody.IsValid() )
			r.Body = ((PhysicsBody3d)targetBody._body).native;

		if ( filterCallback is not null )
		{
			r.FilterDelegate = (IntPtr)((delegate* unmanaged< int, byte >)&FilterFunctionInternal);
			_currentFilterCallback = filterCallback;
		}

		var nativeResults = ThreadTraceVec;
		PhysicsTrace.TraceAll( r, nativeResults );
		var count = nativeResults.Count();

		_currentFilterCallback = default;

		// Pre-size once so a large first trace doesn't repeatedly grow/realloc the backing array
		results.EnsureCapacity( results.Count + count );

		for ( var i = 0; i < count; i++ )
			results.Add( PhysicsTraceResult.From( nativeResults.Element( i ), request.StartShape ) );

		return count;
	}

	public override IEnumerable<GameObject> FindInPhysics( Sphere sphere )
	{
		var results = ThreadQueryResult;
		native.Query( results, sphere.Center, sphere.Radius, 0x07 );
		return FilterQueryResults( results );
	}

	public override IEnumerable<GameObject> FindInPhysics( BBox box )
	{
		var results = ThreadQueryResult;
		native.Query( results, box, 0x07 );
		return FilterQueryResults( results );
	}

	public override unsafe IEnumerable<GameObject> FindInPhysics( Frustum frustum )
	{
		var corners = stackalloc Vector3[8];
		if ( !frustum.TryGetCorners( corners ) )
			return Enumerable.Empty<GameObject>();

		var results = ThreadQueryResult;
		native.Query( results, (IntPtr)corners, 8, 0x07 );
		return FilterQueryResults( results );
	}

	sealed class TraceResultVector
	{
		public CUtlVectorTraceResult Vec = CUtlVectorTraceResult.Create( 32, 32 );
		~TraceResultVector() => Vec.DeleteThis();
	}

	[ThreadStatic] static TraceResultVector _threadTraceVec;
	static CUtlVectorTraceResult ThreadTraceVec
	{
		get
		{
			_threadTraceVec ??= new TraceResultVector();
			_threadTraceVec.Vec.RemoveAll();
			return _threadTraceVec.Vec;
		}
	}

	sealed class TraceQueryResult
	{
		public CQueryResult Vec = CQueryResult.Create();
		~TraceQueryResult() => Vec.DeleteThis();
	}

	[ThreadStatic] static TraceQueryResult _threadQueryResult;
	static CQueryResult ThreadQueryResult
	{
		get
		{
			_threadQueryResult ??= new TraceQueryResult();
			_threadQueryResult.Vec.RemoveAll();
			return _threadQueryResult.Vec;
		}
	}

	public override int FindBodiesInPhysics( Vector3 center, float radius, Span<PhysicsBody> result )
	{
		var queryResult = ThreadQueryResult;
		native.Query( queryResult, center, radius, 0x07 );

		int total = queryResult.Count();
		int written = 0;
		for ( int i = 0; i < total && written < result.Length; i++ )
		{
			var shape = queryResult.Element( i );
			if ( !shape.IsValid() ) continue;
			var body = shape.Body;
			if ( body.IsValid() ) result[written++] = body;
		}
		return written;
	}

	static HashSet<GameObject> FilterQueryResults( CQueryResult results )
	{
		var gameObjects = new HashSet<GameObject>();
		int count = results.Count();

		for ( int i = 0; i < count; ++i )
		{
			var shape = results.Element( i );
			if ( !shape.IsValid() )
				continue;

			var body = shape.Body;
			if ( !body.IsValid() )
				continue;

			var gameObject = body.GameObject;
			if ( !gameObject.IsValid() )
				continue;

			gameObjects.Add( gameObject );
		}

		return gameObjects;
	}
}
