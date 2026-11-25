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
	/// Trace one object's model in its own space (<c>IModel::MeshTrace</c>, the same trace data native's world instances), then
	/// put the hit back in world space (<see cref="ToWorld"/>). False without a hit, or when the ray misses the model's bounds.
	/// </summary>
	static bool TraceObject( SceneObject sceneObject, Model model, in MeshTraceInput r, out MeshTraceOutput output, out Transform transform )
	{
		output = default;
		if ( !ToLocal( sceneObject, model, r, out var local, out transform ) ) return false;
		if ( !model.native.MeshTrace( local, ref output ) ) return false;
		ToWorld( ref output, r, local, transform );
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

	/// <summary>
	/// A hit in an object's space put back in world space as native's instanced trace does
	/// (<c>RayTracingResult_t::TransformFromModelSpaceToWorldSpace</c>): the point on the local ray at the hit distance taken to
	/// world space gives the distance from the start, a point 1000 units along the local normal gives the normal; the hit
	/// position (from the triangle's corners) goes to world space and the trace vertices stay in the model's space.
	/// </summary>
	static void ToWorld( ref MeshTraceOutput output, in MeshTraceInput r, in MeshTraceInput local, in Transform transform )
	{
		var start = (Vector3)local.start;
		var direction = ((Vector3)local.end - start).Normal;
		var localHit = start + direction * output.distance;
		var worldHit = transform.PointToWorld( localHit );

		output.distance = ((Vector3)r.start).Distance( worldHit );
		output.normal = (transform.PointToWorld( localHit + (Vector3)output.normal * 1000.0f ) - worldHit).Normal;
		output.position = transform.PointToWorld( output.position );
		output.transform = transform;
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
			if ( !TraceObject( sceneObject, model, r, out var output, out _ ) ) continue;

			// Every hit becomes a result, so the material reference native handed out is taken either way
			var result = Result.From( this, output, sceneObject );
			if ( best is null || result.Distance < best.Value.Distance ) best = result;
		}

		return best;
	}

	/// <summary>
	/// Every hit on the world's scene objects, as native's world trace gives them: each triangle the ray crosses
	/// (<c>IModel::MeshTraceAll</c> per object), nearest first.
	/// </summary>
	readonly unsafe void TraceObjectsAll( in MeshTraceInput r, List<Result> into )
	{
		var first = into.Count;
		var results = NativeEngine.CUtlVectorMeshTraceOutput.Create( 32, 32 );
		try
		{
			foreach ( var (sceneObject, model) in ObjectsToTrace() )
			{
				if ( !Passes( sceneObject, r ) ) continue;
				if ( !ToLocal( sceneObject, model, r, out var local, out var transform ) ) continue;
				if ( !model.native.MeshTraceAll( local, results ) ) continue;

				var count = results.Count();
				for ( int i = 0; i < count; i++ )
				{
					var output = results.Element( i );
					ToWorld( ref output, r, local, transform );
					into.Add( Result.From( this, output, sceneObject ) );
				}
			}
		}
		finally
		{
			results.DeleteThis();
		}

		into.Sort( first, into.Count - first, Comparer<Result>.Create( ( a, b ) => a.Distance.CompareTo( b.Distance ) ) );
	}
}
