namespace EngineTests;

/// <summary>
/// A culling box lists itself in its world while it exists, which the scene renderer culls by, and every change to it is a
/// new version of the world's list.
/// </summary>
[TestClass]
public class SceneCullingBoxTest
{
	[TestMethod]
	public void ABoxIsListedInItsWorldUntilDeleted()
	{
		var world = new SceneWorld();
		try
		{
			var box = new SceneCullingBox( world, new Transform( new Vector3( 10, 0, 0 ) ), new Vector3( 4 ), SceneCullingBox.CullMode.Inside );
			Assert.IsTrue( box.IsValid );
			CollectionAssert.AreEqual( new[] { box }, world.CullingBoxes );

			var version = world.CullingBoxesVersion;
			box.Size = new Vector3( 8 );
			Assert.AreNotEqual( version, world.CullingBoxesVersion, "a change is a new version" );
			Assert.AreEqual( 1, world.CullingBoxes.Count );

			version = world.CullingBoxesVersion;
			box.Delete();
			Assert.IsFalse( box.IsValid );
			Assert.AreEqual( 0, world.CullingBoxes.Count );
			Assert.AreNotEqual( version, world.CullingBoxesVersion );
		}
		finally
		{
			world.Delete();
		}
	}
}
