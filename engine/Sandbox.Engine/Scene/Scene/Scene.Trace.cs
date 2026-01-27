using NativeEngine;
using System.Collections.Immutable;

namespace Sandbox;

public partial class Scene : GameObject
{
	[ActionGraphIgnore]
	public SceneTrace Trace => new SceneTrace( this );

	internal IEnumerable<SceneTraceResult> RunTraceAll( SceneTrace trace )
	{
		SceneMetrics.RayTraceAll++;

		List<SceneTraceResult> results = new List<SceneTraceResult>();

		if ( trace.NeedsFilterCallback )
		{
			trace.PhysicsTrace.filterCallback = trace.FilterCallback;
		}

		if ( trace.IncludePhysicsWorld )
		{
			var physicsResults = trace.PhysicsTrace.RunAll();

			foreach ( var result in physicsResults )
			{
				var sceneResult = SceneTraceResult.From( this, result );
				results.Add( sceneResult );
			}
		}

		if ( trace.IncludeRenderMeshes && SceneWorld is not null )
		{
			var mt = Engine.Utility.RayTrace.MeshTraceRequest.From( trace.PhysicsTrace.request, SceneWorld, trace.CullMode );
			mt.filterCallback = trace.NeedsFilterCallback ? trace.FilterCallback : default;
			var meshTraceResult = mt.Run();
			if ( meshTraceResult.Hit )
			{
				results.Add( SceneTraceResult.From( this, meshTraceResult ) );
			}
		}

		foreach ( var system in systems )
		{
			if ( system is GameObjectSystem.ITraceProvider traceProvider )
			{
				traceProvider.DoTrace( trace, results );
			}
		}

		return results.OrderBy( r => r.Fraction );
	}

	internal unsafe SceneTraceResult RunTrace( SceneTrace trace )
	{
		SceneMetrics.RayTrace++;

		// pool me
		SceneTraceResult bestResult = default;
		bestResult.Fraction = float.MaxValue;

		if ( trace.NeedsFilterCallback )
		{
			trace.PhysicsTrace.filterCallback = trace.FilterCallback;
		}

		if ( trace.IncludePhysicsWorld )
		{
			var physicsResult = trace.PhysicsTrace.Run();
			bestResult = SceneTraceResult.From( this, physicsResult );
		}

		if ( trace.IncludeRenderMeshes && SceneWorld is not null )
		{
			var mt = Engine.Utility.RayTrace.MeshTraceRequest.From( trace.PhysicsTrace.request, SceneWorld, trace.CullMode );
			mt.filterCallback = trace.NeedsFilterCallback ? trace.FilterCallback : default;
			var meshTraceResult = mt.Run();
			if ( meshTraceResult.Hit )
			{
				var result = SceneTraceResult.From( this, meshTraceResult );

				if ( result.Fraction < bestResult.Fraction )
					bestResult = result;
			}
		}

		foreach ( var system in systems )
		{
			if ( system is GameObjectSystem.ITraceProvider traceProvider )
			{
				var result = traceProvider.DoTrace( trace );

				if ( result.HasValue && result.Value.Fraction < bestResult.Fraction )
					bestResult = result.Value;
			}
		}

		if ( bestResult.Fraction < 2 )
			return bestResult;

		// Empty result
		return new SceneTraceResult
		{
			Scene = trace.scene,
			Hit = false,
			StartedSolid = false,
			StartPosition = trace.PhysicsTrace.request.StartPos,
			EndPosition = trace.PhysicsTrace.request.EndPos,
			HitPosition = trace.PhysicsTrace.request.EndPos,
			Fraction = 1,
			Direction = (trace.PhysicsTrace.request.EndPos - trace.PhysicsTrace.request.StartPos).Normal,
			Tags = default
		};
	}

	/// <summary>
	/// Find game objects in a sphere using physics.
	/// </summary>
	public IEnumerable<GameObject> FindInPhysics( Sphere sphere )
	{
		var results = CQueryResult.Create();
		PhysicsWorld.native.Query( results, sphere.Center, sphere.Radius, 0x07 );
		return FilterQueryResults( results );
	}

	/// <summary>
	/// Find game objects in a box using physics.
	/// </summary>
	public IEnumerable<GameObject> FindInPhysics( BBox box )
	{
		var results = CQueryResult.Create();
		PhysicsWorld.native.Query( results, box, 0x07 );
		return FilterQueryResults( results );
	}

	/// <summary>
	/// Find game objects in a frustum using physics.
	/// </summary>
	public unsafe IEnumerable<GameObject> FindInPhysics( Frustum frustum )
	{
		var corners = stackalloc Vector3[8];
		if ( !frustum.TryGetCorners( corners ) )
			return Enumerable.Empty<GameObject>();

		var results = CQueryResult.Create();
		PhysicsWorld.native.Query( results, (IntPtr)corners, 8, 0x07 );
		return FilterQueryResults( results );
	}

	private HashSet<GameObject> FilterQueryResults( CQueryResult results )
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

		results.DeleteThis();

		return gameObjects;
	}
}

[Expose, ActionGraphIgnore]
public partial struct SceneTrace
{
	internal Scene scene;
	public PhysicsTraceBuilder PhysicsTrace;
	internal bool IncludeHitboxes;
	internal bool IncludeRenderMeshes;
	internal int CullMode;
	internal bool IncludePhysicsWorld;
	internal ImmutableArray<GameObject> IgnoreSingleObject = ImmutableArray<GameObject>.Empty;
	internal ImmutableArray<GameObject> IgnoreHierarchy = ImmutableArray<GameObject>.Empty;

	internal SceneTrace( Scene scene )
	{
		IncludePhysicsWorld = true;
		this.scene = scene;
		PhysicsTrace = scene.PhysicsWorld.Trace;
		CullMode = 2;
	}

	/// <summary>
	/// returns true if we need to do some managed side filtering
	/// </summary>
	internal readonly bool NeedsFilterCallback
	{
		get
		{
			if ( !IgnoreSingleObject.IsEmpty ) return true;
			if ( !IgnoreHierarchy.IsEmpty ) return true;

			return false;
		}
	}

	/// <summary>
	/// Casts a sphere from point A to point B.
	/// </summary>
	public SceneTrace Sphere( float radius, in Vector3 from, in Vector3 to ) => Ray( from, to ).Radius( radius );

	/// <summary>
	/// Casts a sphere from a given position and direction, up to a given distance.
	/// </summary>
	public SceneTrace Sphere( float radius, in Ray ray, in float distance ) => Ray( ray, distance ).Radius( radius );

	/// <summary>
	/// Casts a box from point A to point B.
	/// </summary>
	public SceneTrace Box( Vector3 extents, in Vector3 from, in Vector3 to )
	{
		return Ray( from, to ).Size( extents );
	}

	/// <summary>
	/// Casts a box from a given position and direction, up to a given distance.
	/// </summary>
	public SceneTrace Box( Vector3 extents, in Ray ray, in float distance )
	{
		return Ray( ray, distance ).Size( extents );
	}

	/// <summary>
	/// Casts a box from point A to point B.
	/// </summary>
	public SceneTrace Box( BBox bbox, in Vector3 from, in Vector3 to )
	{
		return Ray( from, to ).Size( bbox );
	}

	/// <summary>
	/// Casts a box from a given position and direction, up to a given distance.
	/// </summary>
	public SceneTrace Box( BBox bbox, in Ray ray, in float distance )
	{
		return Ray( ray, distance ).Size( bbox );
	}

	/// <summary>
	/// Casts a capsule
	/// </summary>
	[ActionGraphInclude, Group( "Shapes" )]
	public readonly SceneTrace Capsule( Capsule capsule )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Capsule( capsule );
		return t;
	}

	/// <summary>
	/// Casts a capsule from point A to point B.
	/// </summary>
	public SceneTrace Capsule( Capsule capsule, in Vector3 from, in Vector3 to )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Capsule( capsule, from, to );
		return t;
	}

	/// <summary>
	/// Casts a capsule from a given position and direction, up to a given distance.
	/// </summary>
	public SceneTrace Capsule( Capsule capsule, in Ray ray, in float distance )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Capsule( capsule, ray, distance );
		return t;
	}

	/// <summary>
	/// Casts a cylinder
	/// </summary>
	[ActionGraphInclude, Group( "Shapes" )]
	public readonly SceneTrace Cylinder( float height, float radius )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Cylinder( height, radius );
		return t;
	}

	/// <summary>
	/// Casts a cylinder from point A to point B.
	/// </summary>
	public SceneTrace Cylinder( float height, float radius, in Vector3 from, in Vector3 to )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Cylinder( height, radius, from, to );
		return t;
	}

	/// <summary>
	/// Casts a cylinder from a given position and direction, up to a given distance.
	/// </summary>
	public SceneTrace Cylinder( float height, float radius, in Ray ray, in float distance )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Cylinder( height, radius, ray, distance );
		return t;
	}

	/// <summary>
	/// Casts a ray from point A to point B.
	/// </summary>
	public SceneTrace Ray( in Vector3 from, in Vector3 to )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Ray( from, to );
		return t;
	}

	/// <summary>
	/// Casts a ray from a given position and direction, up to a given distance.
	/// </summary>
	public SceneTrace Ray( in Ray ray, in float distance )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Ray( ray, distance );
		return t;
	}

	/// <summary>
	/// Casts a <see cref="PhysicsBody"/>.
	/// </summary>
	[ActionGraphInclude, Group( "Shapes" )]
	internal readonly SceneTrace Body( PhysicsBody body )
	{
		return this with
		{
			PhysicsTrace = PhysicsTrace with { targetBody = body }
		};
	}

	/// <summary>
	/// Casts a PhysicsBody from its current position and rotation to desired end point.
	/// </summary>
	public SceneTrace Body( PhysicsBody body, in Vector3 to )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Body( body, to );
		return t;
	}

	/// <summary>
	/// Casts a PhysicsBody from its current position and rotation to desired end point.
	/// </summary>
	public SceneTrace Body( Rigidbody body, in Vector3 to )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Body( body.PhysicsBody, to );
		return t;
	}

	/// <summary>
	/// Casts a PhysicsBody from a position and rotation to desired end point.
	/// </summary>
	public SceneTrace Body( PhysicsBody body, in Transform from, in Vector3 to )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Body( body, from, to );
		return t;
	}

	/// <summary>
	/// Sweeps each <see cref="PhysicsShape">PhysicsShape</see> of given PhysicsBody and returns the closest collision. Does not support Mesh PhysicsShapes.
	/// Basically 'hull traces' but with physics shapes.
	/// Same as tracing a body but allows rotation to change during the sweep.
	/// </summary>
	public SceneTrace Sweep( in PhysicsBody body, in Transform from, in Transform to )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Sweep( body, from, to );
		return t;
	}

	/// <summary>
	/// Sweeps each <see cref="PhysicsShape">PhysicsShape</see> of given PhysicsBody and returns the closest collision. Does not support Mesh PhysicsShapes.
	/// Basically 'hull traces' but with physics shapes.
	/// Same as tracing a body but allows rotation to change during the sweep.
	/// </summary>
	public SceneTrace Sweep( in Rigidbody body, in Transform from, in Transform to )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Sweep( body.PhysicsBody, from, to );
		return t;
	}

	/// <summary>
	/// Creates a Trace.Sweep using the <see cref="PhysicsBody">PhysicsBody</see>'s position as the starting position.
	/// </summary>
	public SceneTrace Sweep( in PhysicsBody body, in Transform to )
	{
		return Sweep( body, body.Transform, to );
	}

	/// <summary>
	/// Sets the start and end positions of the trace request
	/// </summary>
	[ActionGraphInclude, Group( "#Path" )]
	public readonly SceneTrace FromTo( in Vector3 from, in Vector3 to )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.FromTo( from, to );
		return t;
	}

	/// <summary>
	/// Sets the start transform and end position of the trace request
	/// </summary>
	[ActionGraphInclude, Group( "#Path" )]
	public readonly SceneTrace FromTo( in Transform from, in Vector3 to )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.FromTo( from, to );
		return t;
	}

	/// <summary>
	/// Makes this trace an axis aligned box of given size. Extracts mins and maxs from the Bounding Box.
	/// </summary>
	[ActionGraphInclude, Title( "Box" ), Group( "Shapes" )]
	public readonly SceneTrace Size( in BBox hull )
	{
		return Size( hull.Mins, hull.Maxs );
	}

	/// <summary>
	/// Makes this trace an axis aligned box of given size. Calculates mins and maxs by assuming given size is (maxs-mins) and the center is in the middle.
	/// </summary>
	public readonly SceneTrace Size( in Vector3 size )
	{
		return Size( size * -0.5f, size * 0.5f );
	}

	/// <summary>
	/// Makes this trace an axis aligned box of given size.
	/// </summary>
	public readonly SceneTrace Size( in Vector3 mins, in Vector3 maxs )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Size( mins, maxs );
		return t;
	}

	/// <summary>
	/// Makes this a rotated trace, for tracing rotated boxes and capsules.
	/// </summary>
	public readonly SceneTrace Rotated( in Rotation rotation )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Rotated( rotation );
		return t;
	}

	// Named this radius instead of size just incase there's some casting going on and Size gets called instead
	/// <summary>
	/// Makes this trace a sphere of given radius.
	/// </summary>
	[ActionGraphInclude, Title( "Sphere" ), Group( "Shapes" )]
	public readonly SceneTrace Radius( float radius )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.Radius( radius );
		return t;
	}

	/// <summary>
	/// Should we compute hit position.
	/// </summary>
	public readonly SceneTrace UseHitPosition( bool enabled = true )
	{
		var t = this;
		t.PhysicsTrace = PhysicsTrace.UseHitPosition( enabled );
		return t;
	}

	/// <summary>
	/// Should we hit hitboxes
	/// </summary>
	[ActionGraphInclude, Group( "Filters" )]
	public readonly SceneTrace UseHitboxes( bool hit = true )
	{
		var t = this;
		t.IncludeHitboxes = hit;
		return t;
	}

	/// <summary>
	/// Should we hit meshes too? This can be slow and only works for the editor.
	/// </summary>
	[ActionGraphInclude, Group( "Filters" )]
	public readonly SceneTrace UseRenderMeshes( bool hit = true )
	{
		if ( !Application.IsEditor )
		{
			Log.Error( "UseRenderMeshes is only available in edito" );
			return this;
		}
		var t = this;
		t.IncludeRenderMeshes = hit;
		t.CullMode = 2;
		return t;
	}

	/// <summary>
	/// Should we hit meshes too? This can be slow and only works for the editor.
	/// </summary>
	[ActionGraphInclude, Group( "Filters" )]
	public readonly SceneTrace UseRenderMeshes( bool hitFront, bool hitBack )
	{
		if ( !Application.IsEditor )
		{
			Log.Error( "UseRenderMeshes is only available in edito" );
			return this;
		}
		var t = this;
		t.IncludeRenderMeshes = hitFront || hitBack;
		t.CullMode = hitFront && hitBack ? 0 : hitFront ? 2 : 1;
		return t;
	}

	/// <summary>
	/// Should we hit physics objects?
	/// </summary>
	[ActionGraphInclude, Group( "Filters" )]
	public readonly SceneTrace UsePhysicsWorld( bool hit = true )
	{
		var t = this;
		t.IncludePhysicsWorld = hit;
		return t;
	}

	/// <summary>
	/// Only return entities with this tag. Subsequent calls to this will add multiple requirements
	/// and they'll all have to be met (ie, the entity will need all tags).
	/// </summary>
	[ActionGraphInclude, Group( "Filters/#Tags" )]
	public readonly SceneTrace WithTag( string tag ) { var t = this; t.PhysicsTrace = t.PhysicsTrace.WithTag( tag ); return t; }

	/// <summary>
	/// Only return entities with all of these tags
	/// </summary>
	[ActionGraphInclude, Group( "Filters/#Tags" )]
	public readonly SceneTrace WithAllTags( params string[] tags ) { var t = this; t.PhysicsTrace = t.PhysicsTrace.WithAllTags( tags ); return t; }

	/// <summary>
	/// Only return entities with all of these tags
	/// </summary>
	public readonly SceneTrace WithAllTags( ITagSet tags ) { var t = this; t.PhysicsTrace = t.PhysicsTrace.WithAllTags( tags ); return t; }

	/// <summary>
	/// Only return entities with any of these tags
	/// </summary>
	[ActionGraphInclude, Group( "Filters/#Tags" )]
	public readonly SceneTrace WithAnyTags( params string[] tags ) { var t = this; t.PhysicsTrace = t.PhysicsTrace.WithAnyTags( tags ); return t; }

	/// <summary>
	/// Only return entities with any of these tags
	/// </summary>
	public readonly SceneTrace WithAnyTags( ITagSet tags ) { var t = this; t.PhysicsTrace = t.PhysicsTrace.WithAnyTags( tags ); return t; }

	/// <summary>
	/// Only return entities without any of these tags
	/// </summary>
	[ActionGraphInclude, Group( "Filters/#Tags" )]
	public readonly SceneTrace WithoutTags( params string[] tags ) { var t = this; t.PhysicsTrace = t.PhysicsTrace.WithoutTags( tags ); return t; }

	/// <summary>
	/// Only return entities without any of these tags
	/// </summary>
	public readonly SceneTrace WithoutTags( ITagSet tags ) { var t = this; t.PhysicsTrace = t.PhysicsTrace.WithoutTags( tags ); return t; }

	/// <summary>
	/// Use the collision rules of an object with the given tags.
	/// </summary>
	/// <param name="tag">Which tag this trace will adopt the collision rules of.</param>
	/// <param name="asTrigger">If true, trace against triggers only. Otherwise, trace for collisions (default).</param>
	public readonly SceneTrace WithCollisionRules( string tag, bool asTrigger = false ) => this with { PhysicsTrace = PhysicsTrace.WithCollisionRules( tag, asTrigger ) };

	/// <summary>
	/// Use the collision rules for the given set of tags.
	/// </summary>
	/// <param name="tags">Which tags this trace will adopt the collision rules of.</param>
	/// <param name="asTrigger">If true, trace against triggers only. Otherwise, trace for collisions (default).</param>
	public readonly SceneTrace WithCollisionRules( IEnumerable<string> tags, bool asTrigger = false ) => this with { PhysicsTrace = PhysicsTrace.WithCollisionRules( tags, asTrigger ) };

	/// <summary>
	/// Do not hit this object
	/// </summary>
	[ActionGraphInclude, Group( "Filters" )]
	public readonly SceneTrace IgnoreGameObject( GameObject obj ) { var t = this; t.IgnoreSingleObject = t.IgnoreSingleObject.Add( obj ); return t; }

	/// <summary>
	/// Do not hit this object
	/// </summary>
	[ActionGraphInclude, Group( "Filters" )]
	public readonly SceneTrace IgnoreGameObjectHierarchy( GameObject obj ) { var t = this; t.IgnoreHierarchy = t.IgnoreHierarchy.Add( obj ); return t; }


	// TODO - IgnoreGameObject, IgnoreGameObjectHierarchy versions that take multiple objects

	/// <summary>
	/// Hit Triggers
	/// </summary>
	[ActionGraphInclude, Group( "Filters" )]
	public readonly SceneTrace HitTriggers() { var t = this; t.PhysicsTrace = t.PhysicsTrace.HitTriggers(); return t; }

	/// <summary>
	/// Hit Only Triggers
	/// </summary>
	[ActionGraphInclude, Group( "Filters" )]
	public readonly SceneTrace HitTriggersOnly() { var t = this; t.PhysicsTrace = t.PhysicsTrace.HitTriggersOnly(); return t; }

	/// <summary>
	/// Do not hit static objects
	/// </summary>
	[ActionGraphInclude, Group( "Filters" )]
	public readonly SceneTrace IgnoreStatic() { var t = this; t.PhysicsTrace = t.PhysicsTrace.IgnoreStatic(); return t; }

	/// <summary>
	/// Do not hit dynamic objects
	/// </summary>
	[ActionGraphInclude, Group( "Filters" )]
	public readonly SceneTrace IgnoreDynamic() { var t = this; t.PhysicsTrace = t.PhysicsTrace.IgnoreDynamic(); return t; }

	/// <summary>
	/// Do not hit keyframed objects
	/// </summary>
	[ActionGraphInclude, Group( "Filters" )]
	public readonly SceneTrace IgnoreKeyframed() { var t = this; t.PhysicsTrace = t.PhysicsTrace.IgnoreKeyframed(); return t; }

	/// <summary>
	/// Run the trace and return the result. The result will return the first hit.
	/// </summary>
	[ActionGraphInclude, Impure]
	public readonly SceneTraceResult Run()
	{
		return scene.RunTrace( this );
	}

	/// <summary>
	/// Run the trace and record everything we hit along the way. The result will be an array of hits.
	/// </summary>
	[ActionGraphInclude, Impure]
	public readonly IEnumerable<SceneTraceResult> RunAll()
	{
		return scene.RunTraceAll( this );
	}

	/// <summary>
	/// Return true if we should hit this shape.
	/// We puposely keep this locked down, don't offer a user specified callback.
	/// </summary>
	internal readonly bool FilterCallback( PhysicsShape shape )
	{
		var body = shape.Body;

		return FilterCallback( body.GameObject );
	}

	/// <summary>
	/// Return true if we should hit this sceneobject.
	/// We puposely keep this locked down, don't offer a user specified callback.
	/// </summary>
	internal readonly bool FilterCallback( SceneObject so )
	{
		return FilterCallback( so.GameObject );
	}

	internal readonly bool FilterCallback( GameObject go )
	{
		if ( go is null ) return true;

		//
		// We're ignoring this object in particular
		//
		if ( IgnoreSingleObject.Contains( go ) )
			return false;

		//
		// Object is an ancestor of an object we're ignoring the hierachy of
		//

		for ( int i = 0; i < IgnoreHierarchy.Length; i++ )
		{
			if ( go.IsAncestor( IgnoreHierarchy[i] ) ) return false;
		}


		return true;
	}
}

[Expose, ActionGraphIgnore]
public struct SceneTraceResult
{
	public Scene Scene;

	/// <summary>
	/// Whether the trace hit something or not
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "#Result" )]
	public bool Hit;

	/// <summary>
	/// Whether the trace started in a solid
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "#Result" )]
	public bool StartedSolid;

	/// <summary>
	/// The start position of the trace
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Trace" )]
	public Vector3 StartPosition;

	/// <summary>
	/// The end or hit position of the trace
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Trace" )]
	public Vector3 EndPosition;

	/// <summary>
	/// The hit position of the trace. Requires <see cref="SceneTrace.UseHitPosition(bool)"/>.
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "#Result" )]
	public Vector3 HitPosition;

	/// <summary>
	/// The hit surface normal (direction vector)
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "#Result" )]
	public Vector3 Normal;

	/// <summary>
	/// A fraction [0..1] of where the trace hit between the start and the original end positions
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "#Result" )]
	public float Fraction;

	/// <summary>
	/// The GameObject that was hit
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Hit Object" )]
	public GameObject GameObject;

	/// <summary>
	/// The Component that was hit
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Hit Object" )]
	public Component Component;

	/// <summary>
	/// The Collider that was hit
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Hit Object" )]
	public Collider Collider;

	/// <summary>
	/// The physics object that was hit, if any
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Hit Object" )]
	public PhysicsBody Body;

	/// <summary>
	/// The physics shape that was hit, if any
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Hit Object" )]
	public PhysicsShape Shape;

	/// <summary>
	/// The physical properties of the hit surface
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Hit Object" )]
	public Surface Surface;

	/// <summary>
	/// The id of the hit bone (either from hitbox or physics shape)
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Hit Object" )]
	public int Bone;

	/// <summary>
	/// The direction of the trace ray
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Trace" )]
	public Vector3 Direction;

	/// <summary>
	/// The triangle index hit, if we hit a mesh <see cref="PhysicsShape">physics shape</see>
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Hit Object" )]
	public int Triangle;

	/// <summary>
	/// The tags that the hit shape had
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Hit Object" )]
	public string[] Tags;

	/// <summary>
	/// The hitbox that we hit
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "Hit Object" )]
	public Hitbox Hitbox;

	/// <summary>
	/// The distance between start and end positions.
	/// </summary>
	[ActionGraphInclude, ReadOnly, Group( "#Result" )]
	public readonly float Distance => Vector3.DistanceBetween( StartPosition, EndPosition );

	internal PhysicsTrace.Request.Shape StartShape;

	public static SceneTraceResult From( in Scene scene, in PhysicsTraceResult r )
	{
		var result = new SceneTraceResult
		{
			Scene = scene,
			Hit = r.Hit,
			StartedSolid = r.StartedSolid,
			StartPosition = r.StartPosition,
			EndPosition = r.EndPosition,
			HitPosition = r.HitPosition,
			Normal = r.Normal,
			Fraction = r.Fraction,
			Body = r.Body,
			Shape = r.Shape,
			Surface = r.Surface,
			Bone = r.Bone,
			Direction = r.Direction,
			Triangle = r.Triangle,
			Tags = r.Tags,
			GameObject = r.Body?.GameObject,
			Component = r.Body?.Component,
			Collider = r.Shape?.Collider,
			StartShape = r.StartShape,
		};

		return result;
	}

	public static SceneTraceResult From( in Scene scene, in Engine.Utility.RayTrace.MeshTraceRequest.Result r )
	{
		var result = new SceneTraceResult
		{
			Scene = scene,
			Hit = r.Hit,
			StartedSolid = false,
			StartPosition = r.StartPosition,
			EndPosition = r.EndPosition,
			HitPosition = r.HitPosition,
			Normal = r.Normal,
			Fraction = r.Fraction,
			Body = default,
			Shape = default,
			Surface = default,
			Bone = default,
			Direction = (r.EndPosition - r.StartPosition).Normal,
			Triangle = r.HitTriangle,
			Tags = r.SceneObject.Tags.TryGetAll().ToArray(),
			GameObject = r.SceneObject.GameObject,
			Component = r.SceneObject.Component
		};

		return result;
	}
}
