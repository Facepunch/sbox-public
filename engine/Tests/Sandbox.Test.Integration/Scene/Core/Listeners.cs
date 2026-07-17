namespace SceneTests.Core;

/// <summary>
/// Pins the scene sound-listener registry: FindClosestListener picks the nearest
/// registered listener, falls back to the default when none exist, and removal
/// disposes the listener.
/// </summary>
[TestClass]
public class SceneListenerTest
{
	/// <summary>
	/// With no registered listeners the default (possibly null) listener is returned;
	/// with several, the closest to the query point wins. Removing a listener
	/// disposes it and drops it from the registry.
	/// </summary>
	[TestMethod]
	public void FindClosestListener()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		// No listeners registered - falls back to the default listener
		Assert.AreEqual( scene.Listener, scene.FindClosestListener( Vector3.Zero ) );

		var near = scene.AddListener();
		near.Transform = new Transform( new Vector3( 10, 0, 0 ) );

		var far = scene.AddListener();
		far.Transform = new Transform( new Vector3( 500, 0, 0 ) );

		Assert.AreEqual( near, scene.FindClosestListener( Vector3.Zero ) );
		Assert.AreEqual( far, scene.FindClosestListener( new Vector3( 600, 0, 0 ) ) );

		scene.RemoveListener( near );
		Assert.IsFalse( near.IsValid, "Removal should dispose the listener" );
		Assert.IsTrue( far.IsValid, "The remaining listener should be untouched" );
		Assert.AreEqual( far, scene.FindClosestListener( Vector3.Zero ) );
	}
}
