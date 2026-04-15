using Sandbox;
using Sandbox.UI;

namespace UI;

[TestClass]
public class ScenePanelTests
{
	[TestMethod]
	public void Delete_DestroysOwnedScene()
	{
		var panel = new ScenePanel();
		var ownedScene = panel.RenderScene;

		Assert.IsTrue( ownedScene.IsValid() );

		panel.Delete();

		Assert.IsFalse( ownedScene.IsValid() );
	}

	[TestMethod]
	public void Delete_DoesNotDestroyExternalScene()
	{
		var panel = new ScenePanel();
		var externalScene = new Scene();

		panel.RenderScene = externalScene;
		panel.Delete();

		Assert.IsTrue( externalScene.IsValid() );

		externalScene.Destroy();
	}

	[TestMethod]
	public void SetRenderScene_DestroysOwnedSceneWhenReplaced()
	{
		var panel = new ScenePanel();
		var ownedScene = panel.RenderScene;

		Assert.IsTrue( ownedScene.IsValid() );

		var externalScene = new Scene();
		panel.RenderScene = externalScene;

		Assert.IsFalse( ownedScene.IsValid() );
		Assert.IsTrue( externalScene.IsValid() );
		Assert.AreSame( externalScene, panel.RenderScene );

		panel.Delete();
		externalScene.Destroy();
	}

	[TestMethod]
	public void SetRenderScene_SameInstancePreservesOwnership()
	{
		var panel = new ScenePanel();
		var ownedScene = panel.RenderScene;

		// Re-assign the same scene (usually happens on hotload)
		panel.RenderScene = ownedScene;

		// Ownership should be preserved (so Delete() should destroy it here)
		panel.Delete();

		Assert.IsFalse( ownedScene.IsValid() );
	}
}
