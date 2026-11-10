namespace SceneTests;

/// <summary>
/// Proves a plain scene works in this tier without booting the engine: the scene
/// world, physics world and gizmo world are all created lazily, so as long as
/// nothing renders or collides, no native engine systems get touched.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SceneSmokeTest : SceneTest
{
	/// <summary>
	/// A scene should construct, hold objects and destroy without creating any
	/// native worlds.
	/// </summary>
	[TestMethod]
	public void CreateAndDestroyScene()
	{
		var scene = new Scene();

		Assert.IsTrue( scene.IsValid );

		using ( scene.Push() )
		{
			var go = scene.CreateObject();
			go.Name = "Test Object";

			Assert.IsTrue( go.IsValid );
			Assert.AreEqual( scene, go.Scene );
		}

		scene.Destroy();

		Assert.IsFalse( scene.IsValid );
	}

	/// <summary>
	/// The game tick should run without rendering or physics existing.
	/// </summary>
	[TestMethod]
	public void GameTickRuns()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		scene.GameTick();
		scene.GameTick();

		Assert.IsTrue( go.IsValid );

		scene.Destroy();
	}

	/// <summary>
	/// Destroying an object should go through the deferred-delete queue like it does
	/// in a running game.
	/// </summary>
	[TestMethod]
	public void DeferredDestroy()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Destroy();

		Assert.IsTrue( go.IsValid );

		scene.GameTick();

		Assert.IsFalse( go.IsValid );

		scene.Destroy();
	}
}
