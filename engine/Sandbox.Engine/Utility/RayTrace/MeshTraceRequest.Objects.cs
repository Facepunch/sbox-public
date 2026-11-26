using N3 = System.Numerics.Vector3;

namespace Sandbox.Engine.Utility.RayTrace;

public partial struct MeshTraceRequest
{
	/// <summary>
	/// The world's objects a trace can hit: those with a model, as native's world adds an object with a model to its ray trace
	/// environment.
	/// </summary>
	readonly List<(SceneObject Object, Model Model)> ObjectsToTrace()
	{
		var objects = new List<(SceneObject, Model)>();
		lock ( targetWorld.InternalSceneObjects )
		{
			foreach ( var sceneObject in targetWorld.InternalSceneObjects )
			{
				if ( !sceneObject.IsValid() ) continue;
				if ( sceneObject.Model is not { } model || model.native.IsNull ) continue;
				objects.Add( (sceneObject, model) );
			}
		}

		return objects;
	}

	/// <summary>
	/// Native's scene object filter (<c>CFilterTags::ShouldSkipSceneObject</c>): visible, never the view model layer, every
	/// required tag, one of the optional ones, none of the excluded ones, then the request's own filter.
	/// </summary>
	readonly unsafe bool Passes( SceneObject sceneObject, in MeshTraceInput r )
	{
		if ( !sceneObject.RenderingEnabled ) return false;
		if ( sceneObject.Flags.ViewModelLayer ) return false;

		var tags = sceneObject.TagTokens;

		for ( int i = 0; i < 8 && r.tagRequire[i] != 0; i++ )
		{
			if ( !Contains( tags, r.tagRequire[i] ) ) return false;
		}

		if ( r.tagAny[0] != 0 )
		{
			var any = false;
			for ( int i = 0; i < 8 && r.tagAny[i] != 0 && !any; i++ ) any = Contains( tags, r.tagAny[i] );
			if ( !any ) return false;
		}

		for ( int i = 0; i < 8 && r.tagExclude[i] != 0; i++ )
		{
			if ( Contains( tags, r.tagExclude[i] ) ) return false;
		}

		if ( filterCallback is not null )
		{
			try
			{
				if ( !filterCallback( sceneObject ) ) return false;
			}
			catch ( Exception e )
			{
				// As native's filter thunk: a filter that throws lets the object through
				Log.Warning( e, $"Error in trace filter: {e.Message}" );
			}
		}

		return true;
	}

	static bool Contains( IReadOnlyList<uint> tags, uint token )
	{
		for ( int i = 0; i < tags.Count; i++ )
		{
			if ( tags[i] == token ) return true;
		}

		return false;
	}

	/// <summary>
	/// Trace one object's model in its own space (the same triangles for every object of the model), then put the hit back in
	/// world space (<see cref="FromTriangle"/>). False without a hit, or when the ray misses the model's bounds.
	/// </summary>
	static bool TraceObject( SceneObject sceneObject, Model model, in MeshTraceInput r, out Result result )
	{
		result = default;
		if ( !ToLocal( sceneObject, model, r, out var local, out var transform ) ) return false;
		if ( model.TraceTriangles() is not { } triangles ) return false;
		if ( !Nearest( triangles, local, out var triangle, out var t, out var u, out var v ) ) return false;
		result = FromTriangle( r, local, transform, triangles, triangle, t, u, v, sceneObject );
		return true;
	}

	/// <summary>
	/// The request in an object's own space, if the ray reaches the object's model bounds there.
	/// </summary>
	static bool ToLocal( SceneObject sceneObject, Model model, in MeshTraceInput r, out MeshTraceInput local, out Transform transform )
	{
		local = r;
		transform = sceneObject.Transform;
		var scale = transform.Scale;

		// Native's inverse of a matrix with a zero scale is garbage; nothing to hit
		if ( scale.x == 0 || scale.y == 0 || scale.z == 0 ) return false;

		local.start = transform.PointToLocal( r.start );
		local.end = transform.PointToLocal( r.end );
		local.filterDelegate = IntPtr.Zero;
		return SegmentHitsBox( local.start, local.end, model.Bounds.Grow( 1.0f ) );
	}

	/// <summary>Whether the segment from <paramref name="start"/> to <paramref name="end"/> touches <paramref name="box"/>.</summary>
	static bool SegmentHitsBox( in Vector3 start, in Vector3 end, in BBox box )
	{
		var delta = end - start;
		float near = 0.0f, far = 1.0f;

		for ( int axis = 0; axis < 3; axis++ )
		{
			var s = start[axis];
			var d = delta[axis];
			var min = box.Mins[axis];
			var max = box.Maxs[axis];

			if ( MathF.Abs( d ) < 1e-8f )
			{
				if ( s < min || s > max ) return false;
				continue;
			}

			var t0 = (min - s) / d;
			var t1 = (max - s) / d;
			if ( t0 > t1 ) (t0, t1) = (t1, t0);
			near = MathF.Max( near, t0 );
			far = MathF.Min( far, t1 );
			if ( near > far ) return false;
		}

		return true;
	}

	/// <summary>The nearest hit on the world's scene objects, if any.</summary>
	readonly Result? TraceObjects( in MeshTraceInput r )
	{
		Result? best = null;
		foreach ( var (sceneObject, model) in ObjectsToTrace() )
		{
			if ( !Passes( sceneObject, r ) ) continue;
			if ( !TraceObject( sceneObject, model, r, out var result ) ) continue;
			if ( best is null || result.Distance < best.Value.Distance ) best = result;
		}

		return best;
	}

	/// <summary>
	/// Every hit on the world's scene objects, as native's world trace gives them: each triangle the ray crosses on each
	/// object, nearest first.
	/// </summary>
	readonly void TraceObjectsAll( in MeshTraceInput r, List<Result> into )
	{
		var first = into.Count;
		var crossed = new List<(int Triangle, float T, float U, float V)>();
		foreach ( var (sceneObject, model) in ObjectsToTrace() )
		{
			if ( !Passes( sceneObject, r ) ) continue;
			if ( !ToLocal( sceneObject, model, r, out var local, out var transform ) ) continue;
			if ( model.TraceTriangles() is not { } triangles ) continue;

			Vector3 start = local.start, end = local.end;
			var origin = new N3( start.x, start.y, start.z );
			var delta = new N3( end.x, end.y, end.z ) - origin;
			if ( delta.LengthSquared() < 1e-8f ) continue;

			crossed.Clear();
			triangles.All( origin, delta, local.cullMode, ( triangle, t, u, v ) => crossed.Add( (triangle, t, u, v) ) );
			foreach ( var hit in crossed ) into.Add( FromTriangle( r, local, transform, triangles, hit.Triangle, hit.T, hit.U, hit.V, sceneObject ) );
		}

		into.Sort( first, into.Count - first, Comparer<Result>.Create( ( a, b ) => a.Distance.CompareTo( b.Distance ) ) );
	}
}
