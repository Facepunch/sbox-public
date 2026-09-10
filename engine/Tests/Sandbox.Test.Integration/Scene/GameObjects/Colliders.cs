namespace SceneTests.GameObjects;

[TestClass]
public class ColliderTest
{
	[TestMethod]
	public void BoxCollider()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var bc = go.Components.Create<BoxCollider>();

		Assert.IsNull( bc.Rigidbody );

		go.Destroy();
		scene.ProcessDeletes();
	}

	[TestMethod]
	public void BoxCollider_Rigidbody_ColliderFirst()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var bc = go.Components.Create<BoxCollider>();
		var rb = go.Components.Create<Rigidbody>();

		Assert.AreEqual( rb, bc.Rigidbody );
		Assert.AreEqual( 1, rb.PhysicsBody.Shapes.Count() );

		bc.Enabled = false;

		Assert.AreEqual( null, bc.Rigidbody );
		Assert.AreEqual( 0, rb.PhysicsBody.Shapes.Count() );

		go.Destroy();
		scene.ProcessDeletes();
	}

	[TestMethod]
	public void BoxCollider_Rigidbody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var rb = go.Components.Create<Rigidbody>();
		var bc = go.Components.Create<BoxCollider>();

		Assert.AreEqual( rb, bc.Rigidbody );
		Assert.AreEqual( 1, rb.PhysicsBody.Shapes.Count() );

		bc.Enabled = false;

		Assert.AreEqual( null, bc.Rigidbody );
		Assert.AreEqual( 0, rb.PhysicsBody.Shapes.Count() );

		go.Destroy();
		scene.ProcessDeletes();
	}

	[TestMethod]
	public void BoxCollider_Rigidbody_Clone()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var rb = go.Components.Create<Rigidbody>();
		var bc = go.Components.Create<BoxCollider>();

		Assert.AreEqual( rb, bc.Rigidbody );
		Assert.AreEqual( 1, rb.PhysicsBody.Shapes.Count() );

		var cloned = go.Clone( new Vector3( 100, 200, 300 ) );

		Assert.AreEqual( cloned.Components.Get<Rigidbody>(), cloned.Components.Get<Collider>().Rigidbody );
		Assert.AreEqual( 1, cloned.Components.Get<Rigidbody>().PhysicsBody.Shapes.Count() );

		go.Destroy();
		scene.ProcessDeletes();
	}

	[TestMethod]
	public void BoxCollider_Rigidbody_Clone_Disabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var rb = go.Components.Create<Rigidbody>();
		var bc = go.Components.Create<BoxCollider>();

		Assert.AreEqual( rb, bc.Rigidbody );
		Assert.AreEqual( 1, rb.PhysicsBody.Shapes.Count() );

		go.Enabled = false;

		Assert.AreEqual( null, bc.Rigidbody );
		Assert.AreEqual( null, rb.PhysicsBody );

		var cloned = go.Clone( new Vector3( 100, 200, 300 ) );

		Assert.AreEqual( cloned.Components.Get<Rigidbody>(), cloned.Components.Get<Collider>().Rigidbody );
		Assert.AreEqual( 1, cloned.Components.Get<Rigidbody>().PhysicsBody.Shapes.Count() );

		go.Destroy();
		scene.ProcessDeletes();
	}

	[TestMethod]
	public void RigidBody_First()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var rb = go.AddComponent<Rigidbody>();
		var bc = new GameObject( go ).AddComponent<BoxCollider>();

		Assert.IsNotNull( bc.Rigidbody );
		Assert.IsNotNull( bc.PhysicsBody );

		go.Destroy();
		scene.ProcessDeletes();
	}

	[TestMethod]
	public void RigidBody_Second()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var bc = new GameObject( go ).AddComponent<BoxCollider>();
		var rb = go.AddComponent<Rigidbody>();

		Assert.IsNotNull( bc.Rigidbody );
		Assert.IsNotNull( bc.PhysicsBody );

		go.Destroy();
		scene.ProcessDeletes();
	}

	[TestMethod]
	public void RigidBody_StartKeyframe()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var bc = new GameObject( go ).AddComponent<BoxCollider>();

		Assert.IsNull( bc.Rigidbody );
		Assert.IsNotNull( bc.PhysicsBody );
		Assert.IsNotNull( bc.KeyBody );

		go.Destroy();
		scene.ProcessDeletes();
	}

	[TestMethod]
	public void RigidBody_ToKeyframe()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var rb = go.AddComponent<Rigidbody>();
		var bc = new GameObject( go ).AddComponent<BoxCollider>();

		Assert.IsNotNull( bc.Rigidbody );
		Assert.IsNotNull( bc.PhysicsBody );

		rb.Enabled = false;

		Assert.IsNull( bc.Rigidbody );
		Assert.IsNotNull( bc.PhysicsBody );

		go.Destroy();
		scene.ProcessDeletes();
	}

	[TestMethod]
	public void RigidBody_ToKeyframe_ToRigidbody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var rb = go.AddComponent<Rigidbody>();
		var bc = new GameObject( go ).AddComponent<BoxCollider>();

		Assert.IsNotNull( bc.Rigidbody );
		Assert.IsNotNull( bc.PhysicsBody );
		Assert.IsNull( bc.KeyBody );

		rb.Enabled = false;

		Assert.IsNull( bc.Rigidbody );
		Assert.IsNotNull( bc.PhysicsBody );
		Assert.IsNotNull( bc.KeyBody );

		rb.Enabled = true;

		Assert.IsNotNull( bc.Rigidbody );
		Assert.IsNotNull( bc.PhysicsBody );
		Assert.IsNull( bc.KeyBody );

		go.Destroy();
		scene.ProcessDeletes();
	}

	[TestMethod]
	public void Rigidbody_ChildColliderUpdate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		root.Components.Create<Rigidbody>();

		var child = scene.CreateObject();
		child.Parent = root;

		var collider = scene.CreateObject();
		collider.Parent = child;

		var sphereCollider = collider.Components.Create<SphereCollider>();
		var shape = sphereCollider.Shapes.FirstOrDefault();

		Assert.IsTrue( shape.IsSphereShape, "Shape should be a sphere" );
		Assert.AreEqual( Vector3.Zero, shape.Sphere.Center, "Sphere center should be zero" );

		child.WorldPosition = Vector3.Up * 10;

		Assert.AreEqual( Vector3.Up * 10, shape.Sphere.Center, "Sphere center should have updated" );
	}

	[TestMethod]
	public void Collider_FindClosestPoint()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var bc = go.Components.Create<BoxCollider>();
		bc.Center = new Vector3( 100, 100, 100 );
		bc.Scale = new Vector3( 10, 10, 10 );

		var cp = bc.FindClosestPoint( Vector3.Zero );
		Assert.AreNotEqual( Vector3.Zero, cp, "Closest point should not be zero" );

		cp = bc.FindClosestPoint( bc.Center );
		Assert.AreNotEqual( Vector3.Zero, cp, "Overlapped closest point should not be zero" );

		go.Destroy();
		scene.ProcessDeletes();
	}
}
