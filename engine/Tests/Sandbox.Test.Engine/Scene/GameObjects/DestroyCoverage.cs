namespace SceneTests.GameObjects;

/// <summary>
/// Pins destroy edge cases not covered by Destroy.cs: idempotent destroys, the
/// IsDestroyed/IsValid timeline around the deferred delete queue, component
/// teardown, and Clear's handling of DontDestroyOnLoad children during scene
/// transitions.
/// </summary>
[TestClass]
[DoNotParallelize]
public class DestroyCoverageTest : SceneTest
{
	/// <summary>
	/// Calling Destroy twice queues only one delete - the object disappears
	/// exactly once and the second call is a harmless no-op.
	/// </summary>
	[TestMethod]
	public void DestroyIsIdempotent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		go.Destroy();
		go.Destroy();

		scene.ProcessDeletes();

		Assert.IsFalse( go.IsValid );
		Assert.AreEqual( 0, scene.Children.Count );
		Assert.AreEqual( 0, scene.Directory.GameObjectCount );
	}

	/// <summary>
	/// After Destroy the object reports IsDestroyed immediately, but stays
	/// valid and in the scene until the delete queue is flushed by
	/// ProcessDeletes (no full GameTick required).
	/// </summary>
	[TestMethod]
	public void IsDestroyedImmediateValidUntilProcessDeletes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		go.Destroy();

		Assert.IsTrue( go.IsDestroyed );
		Assert.IsTrue( go.IsValid, "the object must stay valid until deletes are processed" );
		Assert.IsNotNull( go.Scene );

		scene.ProcessDeletes();

		Assert.IsTrue( go.IsDestroyed );
		Assert.IsFalse( go.IsValid );
		Assert.IsNull( go.Scene );
	}

	/// <summary>
	/// DestroyImmediate tears down the object's components synchronously - they
	/// are invalid and gone from the list afterwards.
	/// </summary>
	[TestMethod]
	public void DestroyImmediateDestroysComponents()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<DestroyCoverageProbe>();

		go.DestroyImmediate();

		Assert.IsFalse( comp.IsValid );
		Assert.AreEqual( 0, go.Components.Count );
		Assert.AreEqual( 0, scene.Directory.ComponentCount );
	}

	/// <summary>
	/// Clear destroys all components and children synchronously but leaves the
	/// object itself alive and in the scene.
	/// </summary>
	[TestMethod]
	public void ClearDestroysComponentsAndChildren()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<DestroyCoverageProbe>();
		var child = new GameObject( go );

		go.Clear();

		Assert.IsTrue( go.IsValid );
		Assert.IsFalse( comp.IsValid );
		Assert.IsFalse( child.IsValid );
		Assert.AreEqual( 0, go.Components.Count );
		Assert.AreEqual( 0, go.Children.Count );
	}

	/// <summary>
	/// The scene-transition Clear (includeSaved=false) spares children flagged
	/// DontDestroyOnLoad, while a full Clear removes them too.
	/// </summary>
	[TestMethod]
	public void ClearSparesDontDestroyOnLoadChildren()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();

		var keeper = new GameObject( parent, true, "Keeper" );
		keeper.Flags |= GameObjectFlags.DontDestroyOnLoad;

		var doomed = new GameObject( parent, true, "Doomed" );

		parent.Clear( includeSaved: false );

		Assert.IsTrue( keeper.IsValid, "DontDestroyOnLoad children must survive a scene-transition clear" );
		Assert.IsFalse( doomed.IsValid );
		Assert.AreEqual( 1, parent.Children.Count );

		parent.Clear( includeSaved: true );

		Assert.IsFalse( keeper.IsValid, "a full clear removes even DontDestroyOnLoad children" );
		Assert.AreEqual( 0, parent.Children.Count );
	}
}

/// <summary>
/// Bare component used to observe teardown in the destroy tests.
/// </summary>
public class DestroyCoverageProbe : Component
{
}
