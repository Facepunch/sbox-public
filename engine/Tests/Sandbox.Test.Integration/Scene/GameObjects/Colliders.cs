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

	static void AssertBounds( PhysicsBody body, Vector3 center, Vector3 size, string message )
	{
		var bounds = body.GetBounds();
		Assert.IsTrue( bounds.Center.AlmostEqual( center, 0.5f ), $"{message}: expected center {center}, got {bounds.Center}" );
		// Physics bounds include a small skin margin around each shape
		Assert.IsTrue( bounds.Size.AlmostEqual( size, 2f ), $"{message}: expected size {size}, got {bounds.Size}" );
	}

	/// <summary>
	/// A box on a child that's rotated relative to the Rigidbody should have its Center offset rotated with it,
	/// both when the shape is created and when the child rotates afterwards.
	/// </summary>
	[TestMethod]
	public void Rigidbody_RotatedChildBoxCollider_CenterRotates()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var rb = root.Components.Create<Rigidbody>();

		var child = scene.CreateObject();
		child.Parent = root;
		child.LocalRotation = Rotation.FromYaw( 90 );

		var bc = child.Components.Create<BoxCollider>( false );
		bc.Center = new Vector3( 100, 0, 0 );
		bc.Scale = new Vector3( 10, 10, 10 );
		bc.Enabled = true;

		AssertBounds( rb.PhysicsBody, new Vector3( 0, 100, 0 ), 10, "Created" );

		child.LocalRotation = Rotation.FromYaw( 180 );

		AssertBounds( rb.PhysicsBody, new Vector3( -100, 0, 0 ), 10, "Rotated" );

		root.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Same as <see cref="Rigidbody_RotatedChildBoxCollider_CenterRotates"/>, for a box HullCollider.
	/// </summary>
	[TestMethod]
	public void Rigidbody_RotatedChildHullColliderBox_CenterRotates()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var rb = root.Components.Create<Rigidbody>();

		var child = scene.CreateObject();
		child.Parent = root;
		child.LocalRotation = Rotation.FromYaw( 90 );

		var hc = child.Components.Create<HullCollider>( false );
		hc.Type = HullCollider.PrimitiveType.Box;
		hc.Center = new Vector3( 100, 0, 0 );
		hc.BoxSize = new Vector3( 10, 10, 10 );
		hc.Enabled = true;

		AssertBounds( rb.PhysicsBody, new Vector3( 0, 100, 0 ), 10, "Created" );

		child.LocalRotation = Rotation.FromYaw( 180 );

		AssertBounds( rb.PhysicsBody, new Vector3( -100, 0, 0 ), 10, "Rotated" );

		root.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A rotated, non-uniformly scaled child should scale Center and size along its own axes, then rotate them.
	/// </summary>
	[TestMethod]
	public void Rigidbody_RotatedScaledChildBoxCollider_ScalesThenRotates()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var rb = root.Components.Create<Rigidbody>();

		var child = scene.CreateObject();
		child.Parent = root;
		child.LocalRotation = Rotation.FromYaw( 90 );
		child.LocalScale = new Vector3( 2, 1, 1 );

		var bc = child.Components.Create<BoxCollider>( false );
		bc.Center = new Vector3( 100, 0, 0 );
		bc.Scale = new Vector3( 10, 10, 10 );
		bc.Enabled = true;

		AssertBounds( rb.PhysicsBody, new Vector3( 0, 200, 0 ), new Vector3( 10, 20, 10 ), "Created" );

		root.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A box on the Rigidbody's own GameObject has no rotation relative to the body, so its Center is only moved by
	/// the body itself. Guards against the offset being rotated twice.
	/// </summary>
	[TestMethod]
	public void Rigidbody_BoxColliderOnBody_CenterFollowsBody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 0, 0, 50 );
		go.WorldRotation = Rotation.FromYaw( 90 );

		var rb = go.Components.Create<Rigidbody>();
		var bc = go.Components.Create<BoxCollider>( false );
		bc.Center = new Vector3( 100, 0, 0 );
		bc.Scale = new Vector3( 10, 10, 10 );
		bc.Enabled = true;

		AssertBounds( rb.PhysicsBody, new Vector3( 0, 100, 50 ), 10, "Created" );

		bc.Center = new Vector3( 0, 100, 0 );

		AssertBounds( rb.PhysicsBody, new Vector3( -100, 0, 50 ), 10, "Center changed" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A moved and scaled, but unrotated, child should keep placing its box at its position plus its scaled Center,
	/// including with a mirroring negative scale.
	/// </summary>
	[TestMethod]
	public void Rigidbody_TranslatedScaledChildBoxCollider_Center()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var rb = root.Components.Create<Rigidbody>();

		var child = scene.CreateObject();
		child.Parent = root;
		child.LocalPosition = new Vector3( 0, 0, 50 );
		child.LocalScale = 2;

		var bc = child.Components.Create<BoxCollider>( false );
		bc.Center = new Vector3( 100, 0, 0 );
		bc.Scale = new Vector3( 10, 10, 10 );
		bc.Enabled = true;

		AssertBounds( rb.PhysicsBody, new Vector3( 200, 0, 50 ), 20, "Created" );

		child.LocalScale = new Vector3( -1, 1, 1 );

		// Only checking the center: a negative scale currently collapses the box along that axis, a separate issue
		var center = rb.PhysicsBody.GetBounds().Center;
		Assert.IsTrue( center.AlmostEqual( new Vector3( -100, 0, 50 ), 0.5f ), $"Mirrored: expected center (-100,0,50), got {center}" );

		root.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Without a Rigidbody the box goes on the collider's own keyframe body, which already carries its rotation.
	/// Guards against the offset being rotated twice there too.
	/// </summary>
	[TestMethod]
	public void Keyframe_RotatedBoxCollider_CenterFollowsObject()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 0, 0, 50 );
		go.WorldRotation = Rotation.FromYaw( 90 );

		var bc = go.Components.Create<BoxCollider>( false );
		bc.Center = new Vector3( 100, 0, 0 );
		bc.Scale = new Vector3( 10, 10, 10 );
		bc.Enabled = true;

		Assert.IsNull( bc.Rigidbody );
		AssertBounds( bc.PhysicsBody, new Vector3( 0, 100, 50 ), 10, "Created" );

		bc.Center = new Vector3( 0, 100, 0 );

		AssertBounds( bc.PhysicsBody, new Vector3( -100, 0, 50 ), 10, "Center changed" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// The 2D box path takes the same center and rotation, so a rotated child's Center should rotate in-plane there too.
	/// </summary>
	[TestMethod]
	public void Physics2D_RotatedChildBoxCollider_CenterRotates()
	{
		var scene = new Scene { PhysicsMode = ScenePhysicsMode.Physics2D };
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var rb = root.Components.Create<Rigidbody>();

		var child = scene.CreateObject();
		child.Parent = root;
		child.LocalRotation = Rotation.FromYaw( 90 );

		var bc = child.Components.Create<BoxCollider>( false );
		bc.Center = new Vector3( 100, 0, 0 );
		bc.Scale = new Vector3( 10, 10, 10 );
		bc.Enabled = true;

		var center = rb.PhysicsBody.GetBounds().Center;
		Assert.IsTrue( center.WithZ( 0 ).AlmostEqual( new Vector3( 0, 100, 0 ), 0.5f ), $"Created: expected center (0,100), got {center}" );

		root.Destroy();
		scene.ProcessDeletes();
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
