using Sandbox.Navigation;

namespace NavigationTests;

/// <summary>
/// Tile builders own tens of MiB each and a load runs one per core: they must not outlive the navmesh that used them.
/// </summary>
[TestClass]
[DoNotParallelize]
public class NavMeshBuilderPool
{
	[TestMethod]
	public async Task BuildersGoWhenTheNavmeshIsDisposed()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 1400 );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsTrue( NavMesh.PooledBuilders > 0, "The builds left their builders for the next" );

		scene.NavMesh.Dispose();
		Assert.AreEqual( 0, NavMesh.PooledBuilders );
	}

	/// <summary>
	/// A load's builders, one a core, would otherwise stay live through the collection after its loading screen.
	/// </summary>
	[TestMethod]
	public async Task ALoadsBuildersGoWhenItsScreenGoes()
	{
		var screenBefore = LoadingScreen.IsVisible;
		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			LoadingScreen.IsVisible = true;
			scene.Floor( Vector3.Zero, 8000 );
			await scene.NavMesh.Generate( scene.PhysicsWorld );
			if ( NavMesh.PooledBuilders <= NavMesh.IdleBuilders ) Assert.Inconclusive( $"The load ran only {NavMesh.PooledBuilders} builders at once" );

			LoadingScreen.IsVisible = false;
			Assert.AreEqual( NavMesh.IdleBuilders, NavMesh.PooledBuilders, "All but the idle builders went with the screen" );
		}
		finally
		{
			LoadingScreen.IsVisible = screenBefore;
			scene.NavMesh.Dispose();
		}
	}
}
