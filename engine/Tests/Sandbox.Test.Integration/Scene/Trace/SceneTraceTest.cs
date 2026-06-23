namespace SceneTests.Trace;

[TestClass]
public class SceneTraceTest
{
	/// <summary>
	/// Creates a game object with a box collider at the given position. The box is
	/// axis aligned with faces at center ± size/2.
	/// </summary>
	static GameObject CreateBox( Scene scene, Vector3 center, float size = 50f, params string[] tags )
	{
		var go = scene.CreateObject();
		go.Name = $"Box {center}";
		go.WorldPosition = center;

		foreach ( var tag in tags )
		{
			go.Tags.Add( tag );
		}

		var collider = go.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( size );

		return go;
	}

	/// <summary>
	/// A ray that hits a box should report the hit object, the contact point on the
	/// near face, the surface normal facing back along the ray, and a matching fraction.
	/// </summary>
	[TestMethod]
	public void RayHitsBox()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var box = CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var result = scene.Trace.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) ).Run();

		Assert.IsTrue( result.Hit );
		Assert.IsFalse( result.StartedSolid );
		Assert.AreEqual( box, result.GameObject );
		Assert.IsNotNull( result.Collider );
		Assert.IsNotNull( result.Component );
		Assert.IsTrue( result.Body.IsValid() );
		Assert.IsTrue( result.Shape.IsValid() );

		// Near face of the box is at x = 75
		Assert.AreEqual( 75f, result.EndPosition.x, 0.5f );
		Assert.AreEqual( 0.25f, result.Fraction, 0.01f );
		Assert.AreEqual( 75f, result.Distance, 0.5f );
		Assert.IsTrue( result.Normal.AlmostEqual( new Vector3( -1, 0, 0 ), 0.01f ), $"normal was {result.Normal}" );
		Assert.IsTrue( result.Direction.AlmostEqual( new Vector3( 1, 0, 0 ), 0.01f ) );
	}

	/// <summary>
	/// A ray that passes nowhere near the box should miss, with a fraction of 1 and the
	/// end position equal to the requested end of the trace.
	/// </summary>
	[TestMethod]
	public void RayMisses()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var end = new Vector3( 300, 200, 0 );
		var result = scene.Trace.Ray( new Vector3( 0, 200, 0 ), end ).Run();

		Assert.IsFalse( result.Hit );
		Assert.IsNull( result.GameObject );
		Assert.AreEqual( 1f, result.Fraction, 0.001f );
		Assert.IsTrue( result.EndPosition.AlmostEqual( end, 0.5f ) );
	}

	/// <summary>
	/// The Ray(ray, distance) overload should behave the same as the from/to overload.
	/// </summary>
	[TestMethod]
	public void RayFromDirectionAndDistance()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var box = CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var result = scene.Trace.Ray( new Ray( Vector3.Zero, new Vector3( 1, 0, 0 ) ), 300 ).Run();

		Assert.IsTrue( result.Hit );
		Assert.AreEqual( box, result.GameObject );
		Assert.AreEqual( 75f, result.EndPosition.x, 0.5f );
	}

	/// <summary>
	/// FromTo on its own should produce a ray trace between the two points.
	/// </summary>
	[TestMethod]
	public void FromTo()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var box = CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var result = scene.Trace.FromTo( Vector3.Zero, new Vector3( 300, 0, 0 ) ).Run();

		Assert.IsTrue( result.Hit );
		Assert.AreEqual( box, result.GameObject );
	}

	/// <summary>
	/// A trace that starts inside solid geometry should report StartedSolid - for plain
	/// rays as well as swept shapes - hitting at fraction zero.
	/// </summary>
	[TestMethod]
	public void StartedSolid()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var ray = scene.Trace.Ray( new Vector3( 100, 0, 0 ), new Vector3( 300, 0, 0 ) ).Run();
		Assert.IsTrue( ray.StartedSolid );
		Assert.IsTrue( ray.Hit );
		Assert.AreEqual( 0f, ray.Fraction, 0.001f );

		var sphere = scene.Trace.Sphere( 10f, new Vector3( 100, 0, 0 ), new Vector3( 300, 0, 0 ) ).Run();
		Assert.IsTrue( sphere.StartedSolid );

		// A ray that starts outside must not be flagged
		var clear = scene.Trace.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) ).Run();
		Assert.IsFalse( clear.StartedSolid );
	}

	/// <summary>
	/// With several objects along the path, Run should return the nearest hit.
	/// </summary>
	[TestMethod]
	public void RunReturnsNearestHit()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var near = CreateBox( scene, new Vector3( 100, 0, 0 ) );
		CreateBox( scene, new Vector3( 200, 0, 0 ) );

		var result = scene.Trace.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) ).Run();

		Assert.IsTrue( result.Hit );
		Assert.AreEqual( near, result.GameObject );
	}

	/// <summary>
	/// RunAll should return every object along the path, ordered nearest first.
	/// </summary>
	[TestMethod]
	public void RunAllReturnsHitsNearestFirst()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var near = CreateBox( scene, new Vector3( 100, 0, 0 ) );
		var far = CreateBox( scene, new Vector3( 200, 0, 0 ) );

		var results = scene.Trace.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) ).RunAll().ToArray();

		Assert.AreEqual( 2, results.Length );
		Assert.AreEqual( near, results[0].GameObject );
		Assert.AreEqual( far, results[1].GameObject );
		Assert.IsTrue( results[0].Fraction < results[1].Fraction );
	}

	/// <summary>
	/// A sphere trace should stop a radius short of where the equivalent ray would hit.
	/// </summary>
	[TestMethod]
	public void SphereTraceStopsAtRadius()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var ray = scene.Trace.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) ).Run();
		var sphere = scene.Trace.Sphere( 10f, Vector3.Zero, new Vector3( 300, 0, 0 ) ).Run();

		Assert.IsTrue( sphere.Hit );
		Assert.AreEqual( 65f, sphere.EndPosition.x, 0.5f );
		Assert.IsTrue( sphere.Fraction < ray.Fraction );

		// The ray + Radius() route should match the Sphere() helper
		var viaRadius = scene.Trace.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) ).Radius( 10f ).Run();
		Assert.AreEqual( sphere.EndPosition.x, viaRadius.EndPosition.x, 0.1f );

		// And the ray/distance overload too
		var viaRay = scene.Trace.Sphere( 10f, new Ray( Vector3.Zero, new Vector3( 1, 0, 0 ) ), 300 ).Run();
		Assert.AreEqual( sphere.EndPosition.x, viaRay.EndPosition.x, 0.1f );
	}

	/// <summary>
	/// A box trace should stop with its half extents touching the obstacle, through
	/// the extents, BBox and mins/maxs forms of Size.
	/// </summary>
	[TestMethod]
	public void BoxTraceStopsAtExtents()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateBox( scene, new Vector3( 100, 0, 0 ) );

		// 20 unit box -> half extent 10 -> stops at 65
		var viaExtents = scene.Trace.Box( new Vector3( 20 ), Vector3.Zero, new Vector3( 300, 0, 0 ) ).Run();
		Assert.IsTrue( viaExtents.Hit );
		Assert.AreEqual( 65f, viaExtents.EndPosition.x, 0.5f );

		var viaBBox = scene.Trace.Box( new BBox( new Vector3( -10 ), new Vector3( 10 ) ), Vector3.Zero, new Vector3( 300, 0, 0 ) ).Run();
		Assert.AreEqual( 65f, viaBBox.EndPosition.x, 0.5f );

		var viaSize = scene.Trace.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) ).Size( new Vector3( -10 ), new Vector3( 10 ) ).Run();
		Assert.AreEqual( 65f, viaSize.EndPosition.x, 0.5f );

		var viaRayOverload = scene.Trace.Box( new Vector3( 20 ), new Ray( Vector3.Zero, new Vector3( 1, 0, 0 ) ), 300 ).Run();
		Assert.AreEqual( 65f, viaRayOverload.EndPosition.x, 0.5f );
	}

	/// <summary>
	/// A capsule trace should hit, stopping its radius short of the obstacle.
	/// </summary>
	[TestMethod]
	public void CapsuleTrace()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var capsule = new Capsule( new Vector3( 0, 0, -10 ), new Vector3( 0, 0, 10 ), 5 );
		var result = scene.Trace.Capsule( capsule, Vector3.Zero, new Vector3( 300, 0, 0 ) ).Run();

		Assert.IsTrue( result.Hit );
		Assert.AreEqual( 70f, result.EndPosition.x, 0.5f );

		var viaRay = scene.Trace.Capsule( capsule, new Ray( Vector3.Zero, new Vector3( 1, 0, 0 ) ), 300 ).Run();
		Assert.AreEqual( result.EndPosition.x, viaRay.EndPosition.x, 0.1f );
	}

	/// <summary>
	/// Cylinder and cone traces should hit obstacles in their path.
	/// </summary>
	[TestMethod]
	public void CylinderAndConeTrace()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var cylinder = scene.Trace.Cylinder( 20, 5, Vector3.Zero, new Vector3( 300, 0, 0 ) ).Run();
		Assert.IsTrue( cylinder.Hit );
		Assert.IsTrue( cylinder.EndPosition.x < 75f );

		var cone = scene.Trace.Cone( 20, 5, Vector3.Zero, new Vector3( 300, 0, 0 ) ).Run();
		Assert.IsTrue( cone.Hit );
		Assert.IsTrue( cone.EndPosition.x <= 75.5f );
	}

	/// <summary>
	/// A rotated box trace should still hit; rotation must not break the sweep.
	/// </summary>
	[TestMethod]
	public void RotatedBoxTrace()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var box = CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var result = scene.Trace
			.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) )
			.Size( new Vector3( 20 ) )
			.Rotated( Rotation.FromYaw( 45 ) )
			.Run();

		Assert.IsTrue( result.Hit );
		Assert.AreEqual( box, result.GameObject );
	}

	/// <summary>
	/// Tag filters should select which objects the trace can hit: WithTag and
	/// WithAnyTags skip non-matching objects, WithAllTags requires every tag,
	/// WithoutTags rejects matching objects, and the result carries the hit tags.
	/// </summary>
	[TestMethod]
	public void TagFilters()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var near = CreateBox( scene, new Vector3( 100, 0, 0 ), 50, "red", "team" );
		var far = CreateBox( scene, new Vector3( 200, 0, 0 ), 50, "blue" );

		var from = Vector3.Zero;
		var to = new Vector3( 300, 0, 0 );

		Assert.AreEqual( far, scene.Trace.Ray( from, to ).WithTag( "blue" ).Run().GameObject );
		Assert.AreEqual( far, scene.Trace.Ray( from, to ).WithoutTags( "red" ).Run().GameObject );
		Assert.AreEqual( far, scene.Trace.Ray( from, to ).WithAnyTags( "blue", "green" ).Run().GameObject );
		Assert.AreEqual( near, scene.Trace.Ray( from, to ).WithAllTags( "red", "team" ).Run().GameObject );

		// Requiring tags split across different objects matches nothing
		Assert.IsFalse( scene.Trace.Ray( from, to ).WithAllTags( "red", "blue" ).Run().Hit );

		// The result should carry the tags of the shape we hit
		var hit = scene.Trace.Ray( from, to ).Run();
		Assert.IsTrue( hit.HasTag( "red" ) );
		Assert.IsFalse( hit.HasTag( "blue" ) );
	}

	/// <summary>
	/// IgnoreGameObject should skip that exact object, letting the trace pass
	/// through to whatever is behind it. Ignoring everything means a miss.
	/// </summary>
	[TestMethod]
	public void IgnoreGameObject()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var near = CreateBox( scene, new Vector3( 100, 0, 0 ) );
		var far = CreateBox( scene, new Vector3( 200, 0, 0 ) );

		var from = Vector3.Zero;
		var to = new Vector3( 300, 0, 0 );

		var result = scene.Trace.Ray( from, to ).IgnoreGameObject( near ).Run();
		Assert.AreEqual( far, result.GameObject );

		var none = scene.Trace.Ray( from, to ).IgnoreGameObject( near ).IgnoreGameObject( far ).Run();
		Assert.IsFalse( none.Hit );
	}

	/// <summary>
	/// IgnoreGameObject only skips the exact object - a collider on a child must
	/// still be hit. IgnoreGameObjectHierarchy skips the whole subtree.
	/// </summary>
	[TestMethod]
	public void IgnoreGameObjectHierarchy()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		root.Name = "Root";

		var child = CreateBox( scene, new Vector3( 100, 0, 0 ) );
		child.Parent = root;

		var far = CreateBox( scene, new Vector3( 200, 0, 0 ) );

		var from = Vector3.Zero;
		var to = new Vector3( 300, 0, 0 );

		// Ignoring the parent directly doesn't skip the child's collider
		var single = scene.Trace.Ray( from, to ).IgnoreGameObject( root ).Run();
		Assert.AreEqual( child, single.GameObject );

		// Ignoring the hierarchy does
		var hierarchy = scene.Trace.Ray( from, to ).IgnoreGameObjectHierarchy( root ).Run();
		Assert.AreEqual( far, hierarchy.GameObject );
	}

	/// <summary>
	/// Triggers are skipped by default, included with HitTriggers, and with
	/// HitTriggersOnly the trace must ignore solid geometry entirely.
	/// </summary>
	[TestMethod]
	public void Triggers()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var trigger = CreateBox( scene, new Vector3( 100, 0, 0 ) );
		trigger.Components.Get<BoxCollider>().IsTrigger = true;

		var solid = CreateBox( scene, new Vector3( 200, 0, 0 ) );

		var from = Vector3.Zero;
		var to = new Vector3( 300, 0, 0 );

		Assert.AreEqual( solid, scene.Trace.Ray( from, to ).Run().GameObject );
		Assert.AreEqual( trigger, scene.Trace.Ray( from, to ).HitTriggers().Run().GameObject );
		Assert.AreEqual( trigger, scene.Trace.Ray( from, to ).HitTriggersOnly().Run().GameObject );
	}

	/// <summary>
	/// IgnoreDynamic, IgnoreKeyframed and IgnoreStatic should each skip the matching
	/// body type: rigidbodies are dynamic, plain colliders are keyframed, and
	/// colliders marked Static are static.
	/// </summary>
	[TestMethod]
	public void BodyTypeFilters()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var dynamicBox = CreateBox( scene, new Vector3( 100, 0, 0 ) );
		dynamicBox.Components.Create<Rigidbody>();

		var keyframedBox = CreateBox( scene, new Vector3( 200, 0, 0 ) );

		var staticBox = CreateBox( scene, new Vector3( 300, 0, 0 ) );
		staticBox.Components.Get<BoxCollider>().Static = true;

		var from = Vector3.Zero;
		var to = new Vector3( 400, 0, 0 );

		Assert.AreEqual( dynamicBox, scene.Trace.Ray( from, to ).Run().GameObject );

		Assert.AreEqual( keyframedBox,
			scene.Trace.Ray( from, to ).IgnoreDynamic().Run().GameObject );

		Assert.AreEqual( staticBox,
			scene.Trace.Ray( from, to ).IgnoreDynamic().IgnoreKeyframed().Run().GameObject );

		Assert.IsFalse(
			scene.Trace.Ray( from, to ).IgnoreDynamic().IgnoreKeyframed().IgnoreStatic().Run().Hit );
	}

	/// <summary>
	/// UsePhysicsWorld(false) disables the physics query, so nothing is hit and the
	/// empty result reports the requested end position with a fraction of 1.
	/// </summary>
	[TestMethod]
	public void UsePhysicsWorldFalse()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var end = new Vector3( 300, 0, 0 );
		var result = scene.Trace.Ray( Vector3.Zero, end ).UsePhysicsWorld( false ).Run();

		Assert.IsFalse( result.Hit );
		Assert.AreEqual( 1f, result.Fraction, 0.001f );
		Assert.IsTrue( result.EndPosition.AlmostEqual( end, 0.5f ) );

		Assert.AreEqual( 0, scene.Trace.Ray( Vector3.Zero, end ).UsePhysicsWorld( false ).RunAll().Count() );
	}

	/// <summary>
	/// Tracing a physics body should sweep all its shapes, stopping short of obstacles.
	/// </summary>
	[TestMethod]
	public void BodyTrace()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var mover = CreateBox( scene, Vector3.Zero, 20 );
		var rb = mover.Components.Create<Rigidbody>();

		CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var viaBody = scene.Trace.Body( rb.PhysicsBody, new Vector3( 300, 0, 0 ) ).Run();
		Assert.IsTrue( viaBody.Hit );
		Assert.AreEqual( 65f, viaBody.EndPosition.x, 0.5f );

		var viaRigidbody = scene.Trace.Body( rb, new Vector3( 300, 0, 0 ) ).Run();
		Assert.AreEqual( viaBody.EndPosition.x, viaRigidbody.EndPosition.x, 0.1f );

		var viaTransform = scene.Trace.Body( rb.PhysicsBody, Transform.Zero, new Vector3( 300, 0, 0 ) ).Run();
		Assert.AreEqual( viaBody.EndPosition.x, viaTransform.EndPosition.x, 0.1f );
	}

	/// <summary>
	/// Sweeping a physics body between two transforms should collide with obstacles
	/// along the way.
	/// </summary>
	[TestMethod]
	public void SweepTrace()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var mover = CreateBox( scene, Vector3.Zero, 20 );
		var rb = mover.Components.Create<Rigidbody>();

		CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var result = scene.Trace.Sweep( rb.PhysicsBody, Transform.Zero, new Transform( new Vector3( 300, 0, 0 ) ) ).Run();
		Assert.IsTrue( result.Hit );
		Assert.IsTrue( result.EndPosition.x < 75f );

		var viaRigidbody = scene.Trace.Sweep( rb, Transform.Zero, new Transform( new Vector3( 300, 0, 0 ) ) ).Run();
		Assert.AreEqual( result.EndPosition.x, viaRigidbody.EndPosition.x, 0.1f );

		// The single-transform overload starts from the body's current position
		var fromCurrent = scene.Trace.Sweep( rb.PhysicsBody, new Transform( new Vector3( 300, 0, 0 ) ) ).Run();
		Assert.IsTrue( fromCurrent.Hit );
	}

	/// <summary>
	/// FindInPhysics with a sphere should return objects overlapping it and nothing else.
	/// </summary>
	[TestMethod]
	public void FindInPhysicsSphere()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = CreateBox( scene, new Vector3( 100, 0, 0 ) );
		var b = CreateBox( scene, new Vector3( 200, 0, 0 ) );

		var around = scene.FindInPhysics( new Sphere( new Vector3( 100, 0, 0 ), 10 ) ).ToList();
		Assert.AreEqual( 1, around.Count );
		Assert.AreEqual( a, around[0] );

		var everything = scene.FindInPhysics( new Sphere( new Vector3( 150, 0, 0 ), 200 ) ).ToList();
		Assert.AreEqual( 2, everything.Count );
		CollectionAssert.Contains( everything, a );
		CollectionAssert.Contains( everything, b );

		var nothing = scene.FindInPhysics( new Sphere( new Vector3( 0, 500, 0 ), 10 ) ).ToList();
		Assert.AreEqual( 0, nothing.Count );
	}

	/// <summary>
	/// FindInPhysics with a box should return objects overlapping it and nothing else.
	/// </summary>
	[TestMethod]
	public void FindInPhysicsBox()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = CreateBox( scene, new Vector3( 100, 0, 0 ) );
		CreateBox( scene, new Vector3( 200, 0, 0 ) );

		var overlapping = scene.FindInPhysics( new BBox( new Vector3( 90, -10, -10 ), new Vector3( 110, 10, 10 ) ) ).ToList();
		Assert.AreEqual( 1, overlapping.Count );
		Assert.AreEqual( a, overlapping[0] );

		var nothing = scene.FindInPhysics( new BBox( new Vector3( 400, 400, 400 ), new Vector3( 500, 500, 500 ) ) ).ToList();
		Assert.AreEqual( 0, nothing.Count );
	}

	/// <summary>
	/// Disabled or destroyed objects must not be hit by traces.
	/// </summary>
	[TestMethod]
	public void DisabledAndDestroyedObjectsAreNotHit()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var box = CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var from = Vector3.Zero;
		var to = new Vector3( 300, 0, 0 );

		Assert.IsTrue( scene.Trace.Ray( from, to ).Run().Hit );

		box.Enabled = false;
		Assert.IsFalse( scene.Trace.Ray( from, to ).Run().Hit );

		box.Enabled = true;
		Assert.IsTrue( scene.Trace.Ray( from, to ).Run().Hit );

		box.Destroy();
		scene.ProcessDeletes();
		Assert.IsFalse( scene.Trace.Ray( from, to ).Run().Hit );
	}

	/// <summary>
	/// The ITagSet overloads of the tag filters should behave like the string forms.
	/// </summary>
	[TestMethod]
	public void TagFiltersFromTagSet()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var near = CreateBox( scene, new Vector3( 100, 0, 0 ), 50, "red", "team" );
		var far = CreateBox( scene, new Vector3( 200, 0, 0 ), 50, "blue" );

		var from = Vector3.Zero;
		var to = new Vector3( 300, 0, 0 );

		// Use a detached object's tag collection as the ITagSet
		var tagHolder = scene.CreateObject();

		tagHolder.Tags.Add( "blue" );
		Assert.AreEqual( far, scene.Trace.Ray( from, to ).WithAnyTags( tagHolder.Tags ).Run().GameObject );
		Assert.AreEqual( far, scene.Trace.Ray( from, to ).WithAllTags( tagHolder.Tags ).Run().GameObject );

		tagHolder.Tags.RemoveAll();
		tagHolder.Tags.Add( "red" );
		Assert.AreEqual( far, scene.Trace.Ray( from, to ).WithoutTags( tagHolder.Tags ).Run().GameObject );
	}

	/// <summary>
	/// The shape-only builder overloads should set the swept shape while keeping the
	/// path that was already configured.
	/// </summary>
	[TestMethod]
	public void ShapeOnlyOverloads()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var from = Vector3.Zero;
		var to = new Vector3( 300, 0, 0 );
		var capsule = new Capsule( new Vector3( 0, 0, -10 ), new Vector3( 0, 0, 10 ), 5 );

		var viaCapsule = scene.Trace.Ray( from, to ).Capsule( capsule ).Run();
		Assert.IsTrue( viaCapsule.Hit );
		Assert.AreEqual( 70f, viaCapsule.EndPosition.x, 0.5f );

		var viaCylinder = scene.Trace.Ray( from, to ).Cylinder( 20, 5 ).Run();
		Assert.IsTrue( viaCylinder.Hit );
		Assert.IsTrue( viaCylinder.EndPosition.x < 75f );

		var viaCone = scene.Trace.Ray( from, to ).Cone( 20, 5 ).Run();
		Assert.IsTrue( viaCone.Hit );
		Assert.IsTrue( viaCone.EndPosition.x <= 75.5f );
	}

	/// <summary>
	/// The remaining path overloads - Box from a ray, and FromTo taking a transform -
	/// should trace the same path as their plainer siblings.
	/// </summary>
	[TestMethod]
	public void PathOverloads()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var box = CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var viaBoxRay = scene.Trace.Box( new BBox( new Vector3( -10 ), new Vector3( 10 ) ), new Ray( Vector3.Zero, new Vector3( 1, 0, 0 ) ), 300 ).Run();
		Assert.IsTrue( viaBoxRay.Hit );
		Assert.AreEqual( 65f, viaBoxRay.EndPosition.x, 0.5f );

		var viaTransform = scene.Trace.FromTo( Transform.Zero, new Vector3( 300, 0, 0 ) ).Run();
		Assert.IsTrue( viaTransform.Hit );
		Assert.AreEqual( box, viaTransform.GameObject );
	}

	/// <summary>
	/// UseHitPosition should populate HitPosition with the contact point when enabled,
	/// and UseHitboxes must not break a plain collider trace when no hitboxes exist.
	/// </summary>
	[TestMethod]
	public void HitPositionAndHitboxFlags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var from = Vector3.Zero;
		var to = new Vector3( 300, 0, 0 );

		var withPosition = scene.Trace.Ray( from, to ).UseHitPosition( true ).Run();
		Assert.IsTrue( withPosition.Hit );
		Assert.AreEqual( 75f, withPosition.HitPosition.x, 0.5f );

		var withoutPosition = scene.Trace.Ray( from, to ).UseHitPosition( false ).Run();
		Assert.IsTrue( withoutPosition.Hit );

		var withHitboxes = scene.Trace.Ray( from, to ).UseHitboxes().Run();
		Assert.IsTrue( withHitboxes.Hit );
	}

	/// <summary>
	/// WithCollisionRules adopts the collision rules of the given tag - with no special
	/// rules configured everything should still collide.
	/// </summary>
	[TestMethod]
	public void WithCollisionRules()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var box = CreateBox( scene, new Vector3( 100, 0, 0 ) );

		var from = Vector3.Zero;
		var to = new Vector3( 300, 0, 0 );

		var single = scene.Trace.Ray( from, to ).WithCollisionRules( "player" ).Run();
		Assert.AreEqual( box, single.GameObject );

		var multiple = scene.Trace.Ray( from, to ).WithCollisionRules( new[] { "player", "npc" } ).Run();
		Assert.AreEqual( box, multiple.GameObject );
	}

	/// <summary>
	/// The ignore filters must also apply to RunAll, which uses a separate native
	/// filter path from Run.
	/// </summary>
	[TestMethod]
	public void RunAllRespectsIgnores()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var near = CreateBox( scene, new Vector3( 100, 0, 0 ) );
		var far = CreateBox( scene, new Vector3( 200, 0, 0 ) );

		var results = scene.Trace
			.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) )
			.IgnoreGameObject( near )
			.RunAll()
			.ToArray();

		Assert.AreEqual( 1, results.Length );
		Assert.AreEqual( far, results[0].GameObject );
	}

	/// <summary>
	/// When a collider sits on a child of a rigidbody, the physics shape's body belongs
	/// to the parent - ignoring that parent must skip the shape even though the
	/// collider's own object isn't ignored.
	/// </summary>
	[TestMethod]
	public void IgnoreRigidbodyParentOfCollider()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.Name = "Rigidbody Parent";
		parent.WorldPosition = new Vector3( 100, 0, 0 );
		parent.Components.Create<Rigidbody>();

		var child = scene.CreateObject();
		child.Parent = parent;
		var collider = child.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 50 );

		var far = CreateBox( scene, new Vector3( 200, 0, 0 ) );

		var result = scene.Trace
			.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) )
			.IgnoreGameObject( parent )
			.Run();

		Assert.AreEqual( far, result.GameObject );
	}

	/// <summary>
	/// FindInPhysics with a frustum should return objects inside it and respect the
	/// far plane.
	/// </summary>
	[TestMethod]
	public void FindInPhysicsFrustum()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var near = CreateBox( scene, new Vector3( 100, 0, 0 ) );
		CreateBox( scene, new Vector3( 200, 0, 0 ) );

		// A camera-style frustum at the origin looking down +x, ending before the second box
		var frustum = Frustum.FromCorners(
			new Ray( Vector3.Zero, new Vector3( 1, 0.3f, 0.3f ).Normal ),
			new Ray( Vector3.Zero, new Vector3( 1, -0.3f, 0.3f ).Normal ),
			new Ray( Vector3.Zero, new Vector3( 1, -0.3f, -0.3f ).Normal ),
			new Ray( Vector3.Zero, new Vector3( 1, 0.3f, -0.3f ).Normal ),
			1f, 150f );

		var found = scene.FindInPhysics( frustum ).ToList();

		Assert.AreEqual( 1, found.Count );
		Assert.AreEqual( near, found[0] );
	}

	/// <summary>
	/// FindBodiesInPhysics should write overlapping bodies into the caller's buffer
	/// and return how many were written.
	/// </summary>
	[TestMethod]
	public void FindBodiesInPhysics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = CreateBox( scene, new Vector3( 100, 0, 0 ) );
		CreateBox( scene, new Vector3( 500, 0, 0 ) );

		var buffer = new PhysicsBody[8];
		var written = scene.FindBodiesInPhysics( new Vector3( 100, 0, 0 ), 10f, buffer );

		Assert.AreEqual( 1, written );
		Assert.IsTrue( buffer[0].IsValid() );
		Assert.AreEqual( a, buffer[0].GameObject );

		// A buffer smaller than the hit count must not overflow
		var tiny = new PhysicsBody[1];
		var clamped = scene.FindBodiesInPhysics( new Vector3( 300, 0, 0 ), 1000f, tiny );
		Assert.AreEqual( 1, clamped );
	}

	/// <summary>
	/// Systems implementing ITraceProvider should contribute hits: Run picks the
	/// provider's result when it's nearer than physics, ignores it when it's further,
	/// and RunAll merges it into the sorted result list.
	/// </summary>
	[TestMethod]
	public void TraceProviderSystem()
	{
		// Scene systems are created from Game.TypeLibrary, which doesn't contain the
		// test assembly by default - swap in one that does so TestTraceProviderSystem
		// gets instantiated for this scene.
		var oldTypeLibrary = Game.TypeLibrary;
		var typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		typeLibrary.AddAssembly( typeof( SceneTraceTest ).Assembly, false );
		Game.TypeLibrary = typeLibrary;

		try
		{
			var scene = new Scene();
			using var sceneScope = scene.Push();

			CreateBox( scene, new Vector3( 100, 0, 0 ) ); // physics hit at fraction 0.25

			var from = Vector3.Zero;
			var to = new Vector3( 300, 0, 0 );

			// Provider hit nearer than the physics hit wins
			TestTraceProviderSystem.ProvideHits = true;
			TestTraceProviderSystem.HitFraction = 0.1f;

			var nearer = scene.Trace.Ray( from, to ).Run();
			Assert.IsTrue( nearer.Hit );
			Assert.AreEqual( 0.1f, nearer.Fraction, 0.001f );

			// Provider hit further than the physics hit loses
			TestTraceProviderSystem.HitFraction = 0.9f;

			var further = scene.Trace.Ray( from, to ).Run();
			Assert.AreEqual( 0.25f, further.Fraction, 0.01f );

			// RunAll merges and sorts provider results with physics results
			var all = scene.Trace.Ray( from, to ).RunAll().ToArray();
			Assert.AreEqual( 2, all.Length );
			Assert.AreEqual( 0.25f, all[0].Fraction, 0.01f );
			Assert.AreEqual( 0.9f, all[1].Fraction, 0.001f );
		}
		finally
		{
			TestTraceProviderSystem.ProvideHits = false;
			Game.TypeLibrary = oldTypeLibrary;
		}
	}
}

/// <summary>
/// A trace provider used by <see cref="SceneTraceTest.TraceProviderSystem"/> to exercise
/// the ITraceProvider extension point. Scene systems are instantiated for every scene in
/// the whole test run, so this stays inert unless a test switches it on.
/// </summary>
public class TestTraceProviderSystem : GameObjectSystem, GameObjectSystem.ITraceProvider
{
	public static bool ProvideHits;
	public static float HitFraction = 0.1f;

	public TestTraceProviderSystem( Scene scene ) : base( scene )
	{
	}

	public void DoTrace( in SceneTrace trace, System.Collections.Generic.List<SceneTraceResult> results )
	{
		if ( !ProvideHits )
			return;

		results.Add( MakeResult( trace ) );
	}

	public SceneTraceResult? DoTrace( in SceneTrace trace )
	{
		if ( !ProvideHits )
			return null;

		return MakeResult( trace );
	}

	SceneTraceResult MakeResult( in SceneTrace trace )
	{
		var start = trace.PhysicsTrace.request.StartPos;
		var end = trace.PhysicsTrace.request.EndPos;

		return new SceneTraceResult
		{
			Scene = Scene,
			Hit = true,
			Fraction = HitFraction,
			StartPosition = start,
			EndPosition = start + (end - start) * HitFraction,
			HitPosition = start + (end - start) * HitFraction,
			Direction = (end - start).Normal,
		};
	}
}
