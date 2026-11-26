using Editor;
using SceneTests;

namespace EditorTests;

/// <summary>
/// A project job that requests the latest possible order.
/// </summary>
public sealed class LateCompileComponent : Component, ISceneCompile
{
	SceneCompileWork ISceneCompile.GetWork() => new( "Late bake", "Late bake", _ => Task.CompletedTask, Order: int.MaxValue );
}

/// <summary>
/// Checks job discovery against the systems actually installed in an editor scene.
/// </summary>
[TestClass, DoNotParallelize]
public sealed class SceneCompileJobsTest : SceneTest
{
	/// <summary>
	/// Navigation comes from one scene system. Even the latest project job precedes the runtime write.
	/// </summary>
	[TestMethod]
	public void DiscoversSystemsAndKeepsRuntimeWorkLast()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var component = scene.CreateObject().AddComponent<LateCompileComponent>();
			var runtimeWork = new SceneCompileWork( SceneCompiler.Group, "Write runtime scene", _ => Task.CompletedTask );
			var jobs = SceneCompileSession.DiscoverJobs( scene, runtimeWork ).ToArray();

			var nav = jobs.Single( job => job.Work.Group == "Navigation mesh" );
			Assert.AreSame( scene.GetSystem<NavMeshGameSystem>(), nav.Owner );
			Assert.IsFalse( nav.CanRun );
			Assert.AreEqual( "navmesh/baked.navdata", nav.Work.Outputs.Single() );
			Assert.AreSame( component, jobs[^2].Owner );
			Assert.AreSame( runtimeWork, jobs[^1].Work );

			component.Enabled = false;
			jobs = SceneCompileSession.DiscoverJobs( scene, runtimeWork ).ToArray();
			Assert.IsFalse( jobs.Single( job => job.Owner == component ).CanRun );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
