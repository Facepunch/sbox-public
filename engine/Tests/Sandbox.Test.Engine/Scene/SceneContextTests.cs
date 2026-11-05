namespace SceneTests;

[TestClass]
[DoNotParallelize]
public class SceneContextTests : SceneTest
{
	[TestMethod]
	public void UnlistedScenesAreNotInAll()
	{
		var listed = new Scene();
		var unlisted = new Scene { IsUnlisted = true };

		try
		{
			Assert.IsTrue( Scene.All.Contains( listed ) );
			Assert.IsFalse( Scene.All.Contains( unlisted ), "An unlisted scene, like the menu's, was visible to game code" );
			Assert.IsTrue( Scene.AllIncludingUnlisted.Contains( unlisted ) );
		}
		finally
		{
			listed.Destroy();
			unlisted.Destroy();
		}
	}
}
