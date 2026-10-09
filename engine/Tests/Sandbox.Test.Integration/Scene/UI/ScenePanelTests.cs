using Sandbox;
using Sandbox.UI;

namespace SceneTests.UI;

[TestClass]
public class ScenePanelTest
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
	public void DeleteParent_DestroysOwnedScene()
	{
		var parent = new Panel();
		var panel = parent.AddChild<ScenePanel>();
		var ownedScene = panel.RenderScene;

		Assert.IsTrue( ownedScene.IsValid() );

		// Deleting the parent tears down its children without calling their Delete
		parent.Delete( true );

		Assert.IsFalse( ownedScene.IsValid() );
		Assert.IsNull( panel.RenderScene );
	}

	[TestMethod]
	public void DeleteParent_DoesNotDestroyExternalScene()
	{
		var parent = new Panel();
		var panel = parent.AddChild<ScenePanel>();
		var externalScene = new Scene();

		panel.RenderScene = externalScene;
		parent.Delete( true );

		Assert.IsTrue( externalScene.IsValid() );

		externalScene.Destroy();
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
