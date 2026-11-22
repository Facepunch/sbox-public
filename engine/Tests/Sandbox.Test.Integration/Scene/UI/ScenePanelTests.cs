using Sandbox;
using Sandbox.UI;

namespace SceneTests.UI;

[TestClass]
public class ScenePanelTest
{
	/// <summary>
	/// Deleting a preview during its scene tick releases the scene and ends the panel's render work.
	/// </summary>
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void DeleteDuringTickReleasesPreview( bool duringFrameStart )
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 100, 100 ) };
		var panel = root.AddChild<ScenePanel>();
		panel.Style.Set( "width: 100px; height: 100px;" );
		var scene = panel.RenderScene;
		var frameStart = Sandbox.Tasks.SyncContext.FrameStage.Update;

		void DeletePreview() => panel.Delete( true );

		try
		{
			root.Layout();
			scene.UpdateTime( 0.1 );

			if ( duringFrameStart )
			{
				frameStart.Enqueue( DeletePreview );
			}
			else
			{
				scene.AddHook( GameObjectSystem.Stage.PhysicsStep, 0, DeletePreview, nameof( ScenePanelTest ), nameof( DeletePreview ) );
			}

			panel.Tick();

			Assert.IsFalse( scene.IsValid );
			Assert.IsNull( panel.RenderScene );
			Assert.IsNull( panel.RenderTexture );
		}
		finally
		{
			frameStart.Remove( DeletePreview );
			root.Delete( true );
		}
	}

	[TestMethod]
	public void OwnedSceneIsNotInAll()
	{
		var preview = new ScenePanel();
		var assigned = new Scene();
		var other = new ScenePanel { RenderScene = assigned };

		try
		{
			Assert.IsFalse( Scene.All.Contains( preview.RenderScene ), "A preview scene, like the menu's, was visible to game code" );
			Assert.IsTrue( Scene.All.Contains( assigned ), "A scene given to a ScenePanel lost its visibility" );
		}
		finally
		{
			preview.Delete();
			other.Delete();
			assigned.Destroy();
		}
	}

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
