namespace SceneTests.GameObjects;

/// <summary>
/// Pins the transform proxy mechanism: a TransformProxyComponent takes over the
/// object's transform while enabled, routes sets through the component, and hands
/// control back when disabled.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TransformProxyTest : SceneTest
{
	/// <summary>
	/// While the proxy component is enabled the object's local and world transforms
	/// come from the proxy; disabling it restores the underlying transform.
	/// </summary>
	[TestMethod]
	public void ProxyOverridesTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.WorldPosition = new Vector3( 1000, 0, 0 );

		var go = new GameObject( parent );
		go.LocalPosition = new Vector3( 5, 0, 0 );

		var proxy = go.Components.Create<FixedOffsetProxy>();
		proxy.ProxyLocal = new Transform( new Vector3( 100, 0, 0 ) );

		// Reads route through the proxy
		Assert.IsTrue( go.LocalTransform.Position.AlmostEqual( new Vector3( 100, 0, 0 ) ), $"{go.LocalTransform.Position}" );

		// The default world implementation composes the proxy local with the parent
		Assert.IsTrue( go.WorldPosition.AlmostEqual( new Vector3( 1100, 0, 0 ) ), $"{go.WorldPosition}" );

		// Disabling hands the transform back
		proxy.Enabled = false;

		Assert.IsTrue( go.LocalTransform.Position.AlmostEqual( new Vector3( 5, 0, 0 ) ), $"{go.LocalTransform.Position}" );
		Assert.IsTrue( go.WorldPosition.AlmostEqual( new Vector3( 1005, 0, 0 ) ), $"{go.WorldPosition}" );
	}

	/// <summary>
	/// Setting the transform while proxied routes through the component's
	/// SetLocalTransform/SetWorldTransform hooks instead of the real transform.
	/// </summary>
	[TestMethod]
	public void SetsRouteThroughProxy()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var proxy = go.Components.Create<FixedOffsetProxy>();
		proxy.ProxyLocal = new Transform( new Vector3( 100, 0, 0 ) );

		go.LocalTransform = new Transform( new Vector3( 42, 0, 0 ) );
		Assert.AreEqual( 1, proxy.LocalSets, "the local set should have routed to the proxy" );

		go.WorldTransform = new Transform( new Vector3( 7, 0, 0 ) );
		Assert.AreEqual( 1, proxy.WorldSets, "the world set should have routed to the proxy" );

		// The proxy ignored those sets, so reads still come from the proxy value
		Assert.IsTrue( go.LocalTransform.Position.AlmostEqual( new Vector3( 100, 0, 0 ) ) );
	}

	/// <summary>
	/// Children compose their world transform on top of the proxied parent, and
	/// MarkTransformChanged propagates the change notification.
	/// </summary>
	[TestMethod]
	public void ChildrenFollowProxiedParent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var proxy = go.Components.Create<FixedOffsetProxy>();
		proxy.ProxyLocal = new Transform( new Vector3( 100, 0, 0 ) );

		var child = new GameObject( go );
		child.LocalPosition = new Vector3( 0, 10, 0 );

		Assert.IsTrue( child.WorldPosition.AlmostEqual( new Vector3( 100, 10, 0 ) ), $"{child.WorldPosition}" );

		proxy.ProxyLocal = new Transform( new Vector3( 200, 0, 0 ) );
		proxy.MarkTransformChanged();

		Assert.IsTrue( child.WorldPosition.AlmostEqual( new Vector3( 200, 10, 0 ) ), $"{child.WorldPosition}" );
	}

	/// <summary>
	/// Proxy fixture returning a configurable local transform and counting the
	/// set-calls routed through it.
	/// </summary>
	public class FixedOffsetProxy : TransformProxyComponent
	{
		public Transform ProxyLocal = global::Transform.Zero;
		public int LocalSets;
		public int WorldSets;

		public override Transform GetLocalTransform() => ProxyLocal;
		public override void SetLocalTransform( in Transform value ) => LocalSets++;
		public override void SetWorldTransform( Transform value ) => WorldSets++;
	}
}
