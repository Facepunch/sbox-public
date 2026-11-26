namespace SceneTests;

[TestClass]
public class SceneCameraProjectionTest
{
	[TestMethod]
	public void ProjectionFollowsEveryCameraChange()
	{
		using var camera = new SceneCamera( "test" ) { Size = new Vector2( 100, 100 ), FieldOfView = 90, ZNear = 1, ZFar = 1000 };
		var point = new Vector3( 100, 20, 10 );

		var before = camera.ToScreenNormal( point );
		camera.Position = camera.Position;
		camera.Rotation = camera.Rotation;
		Assert.AreEqual( before, camera.ToScreenNormal( point ), "setting the same values changed the projection" );

		camera.Position = new Vector3( 0, 10, 0 );
		var moved = camera.ToScreenNormal( point );
		Assert.AreNotEqual( before, moved );

		camera.Angles = new Angles( 0, 10, 0 );
		var turned = camera.ToScreenNormal( point );
		Assert.AreNotEqual( moved, turned );

		camera.FieldOfView = 60;
		var zoomed = camera.ToScreenNormal( point );
		Assert.AreNotEqual( turned, zoomed );

		camera.Ortho = true;
		camera.OrthoHeight = 100;
		var ortho = camera.ToScreenNormal( point );
		camera.OrthoHeight = 200;
		Assert.AreNotEqual( ortho, camera.ToScreenNormal( point ) );
	}
}
