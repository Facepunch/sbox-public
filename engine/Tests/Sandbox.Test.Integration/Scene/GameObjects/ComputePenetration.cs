namespace SceneTests.GameObjects;

[TestClass]
public class ComputePenetrationTest
{
	const float Tolerance = 1.0f;

	static BoxCollider CreateBox( Scene scene, Vector3 center, float size = 50f )
	{
		var go = scene.CreateObject();
		go.WorldPosition = center;

		var collider = go.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( size );

		return collider;
	}

	static SphereCollider CreateSphere( Scene scene, Vector3 center, float radius )
	{
		var go = scene.CreateObject();
		go.WorldPosition = center;

		var collider = go.Components.Create<SphereCollider>();
		collider.Radius = radius;

		return collider;
	}

	static PhysicsBody CreateBoxBody( Scene scene, Vector3 center, float size = 50f )
	{
		var go = scene.CreateObject();
		go.WorldPosition = center;

		var rigidbody = go.Components.Create<Rigidbody>();
		var collider = go.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( size );

		return rigidbody.PhysicsBody;
	}

	static void AssertDirection( Vector3 expected, Vector3 actual )
	{
		Assert.AreEqual( expected.x, actual.x, 0.05f );
		Assert.AreEqual( expected.y, actual.y, 0.05f );
		Assert.AreEqual( expected.z, actual.z, 0.05f );
	}

	/// <summary>
	/// Two overlapping boxes report a penetration along the overlapping axis, with the
	/// distance equal to the overlap and the direction pushing the first box clear.
	/// </summary>
	[TestMethod]
	public void OverlappingBoxesReturnsTrue()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = CreateBox( scene, Vector3.Zero );
		var b = CreateBox( scene, new Vector3( 40, 0, 0 ) );

		Assert.IsTrue( a.ComputePenetration( b, out var direction, out var distance ) );
		Assert.AreEqual( 10f, distance, Tolerance );
		AssertDirection( new Vector3( -1, 0, 0 ), direction );
	}

	/// <summary>
	/// Separated boxes don't overlap, so the result is false and the outputs are left empty.
	/// </summary>
	[TestMethod]
	public void SeparatedBoxesReturnsFalse()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = CreateBox( scene, Vector3.Zero );
		var b = CreateBox( scene, new Vector3( 100, 0, 0 ) );

		Assert.IsFalse( a.ComputePenetration( b, out var direction, out var distance ) );
		Assert.AreEqual( Vector3.Zero, direction );
		Assert.AreEqual( 0f, distance );
	}

	/// <summary>
	/// When boxes overlap on multiple axes the result is the smallest move out, along the
	/// axis with the least overlap.
	/// </summary>
	[TestMethod]
	public void PicksShortestAxis()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = CreateBox( scene, Vector3.Zero );
		var b = CreateBox( scene, new Vector3( 45, 10, 0 ) );

		Assert.IsTrue( a.ComputePenetration( b, out var direction, out var distance ) );
		Assert.AreEqual( 5f, distance, Tolerance );
		AssertDirection( new Vector3( -1, 0, 0 ), direction );
	}

	/// <summary>
	/// A deep overlap reports the full overlap distance on the shortest axis.
	/// </summary>
	[TestMethod]
	public void DeepOverlap()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = CreateBox( scene, Vector3.Zero );
		var b = CreateBox( scene, new Vector3( 5, 0, 0 ) );

		Assert.IsTrue( a.ComputePenetration( b, out var direction, out var distance ) );
		Assert.AreEqual( 45f, distance, Tolerance );
		AssertDirection( new Vector3( -1, 0, 0 ), direction );
	}

	/// <summary>
	/// Two overlapping spheres report the overlap of their radii along the line between them.
	/// </summary>
	[TestMethod]
	public void OverlappingSpheres()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = CreateSphere( scene, Vector3.Zero, 32f );
		var b = CreateSphere( scene, new Vector3( 40, 0, 0 ), 32f );

		Assert.IsTrue( a.ComputePenetration( b, out var direction, out var distance ) );
		Assert.AreEqual( 24f, distance, Tolerance );
		AssertDirection( new Vector3( -1, 0, 0 ), direction );
	}

	/// <summary>
	/// A box and a sphere of different shape types collide correctly, and querying from either
	/// side returns the same distance with opposite directions.
	/// </summary>
	[TestMethod]
	public void BoxAndSphereMixedTypes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var box = CreateBox( scene, Vector3.Zero );
		var sphere = CreateSphere( scene, new Vector3( 50, 0, 0 ), 32f );

		Assert.IsTrue( box.ComputePenetration( sphere, out var boxDirection, out var boxDistance ) );
		Assert.AreEqual( 7f, boxDistance, Tolerance );
		AssertDirection( new Vector3( -1, 0, 0 ), boxDirection );

		Assert.IsTrue( sphere.ComputePenetration( box, out var sphereDirection, out var sphereDistance ) );
		Assert.AreEqual( 7f, sphereDistance, Tolerance );
		AssertDirection( new Vector3( 1, 0, 0 ), sphereDirection );
	}

	/// <summary>
	/// Querying the penetration from either body gives the same distance and opposite directions.
	/// </summary>
	[TestMethod]
	public void DirectionIsSymmetric()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = CreateBox( scene, Vector3.Zero );
		var b = CreateBox( scene, new Vector3( 0, 30, 0 ) );

		Assert.IsTrue( a.ComputePenetration( b, out var forward, out var forwardDistance ) );
		Assert.IsTrue( b.ComputePenetration( a, out var backward, out var backwardDistance ) );

		Assert.AreEqual( forwardDistance, backwardDistance, Tolerance );
		AssertDirection( -forward, backward );
	}

	/// <summary>
	/// The transform overload tests against a supplied transform rather than the body's real
	/// position, and doesn't move anything.
	/// </summary>
	[TestMethod]
	public void TransformOverrideUsesPassedTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = CreateBoxBody( scene, Vector3.Zero );
		var b = CreateBoxBody( scene, new Vector3( 200, 0, 0 ) );

		// At its real position b is far away, so there's no penetration.
		Assert.IsFalse( a.ComputePenetration( b, out _, out _ ) );

		// Placing b at an overlapping transform reports a penetration without moving b.
		Assert.IsTrue( a.ComputePenetration( b, new Transform( new Vector3( 40, 0, 0 ) ), out var direction, out var distance ) );
		Assert.AreEqual( 10f, distance, Tolerance );
		AssertDirection( new Vector3( -1, 0, 0 ), direction );

		Assert.AreEqual( 200f, b.Position.x, Tolerance );
	}

	/// <summary>
	/// Translating the first body by direction * distance separates the two bodies.
	/// </summary>
	[TestMethod]
	public void TranslatingByResultSeparates()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = CreateBoxBody( scene, Vector3.Zero );
		var b = CreateBoxBody( scene, new Vector3( 40, 0, 0 ) );

		Assert.IsTrue( a.ComputePenetration( b, out var direction, out var distance ) );

		a.Position += direction * (distance + 1f);

		Assert.IsFalse( a.ComputePenetration( b, out _, out _ ) );
	}
}
