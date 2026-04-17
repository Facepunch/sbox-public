namespace GameObjects;

[TestClass]
[DoNotParallelize]
public class Transforms
{
	[TestMethod]
	public void LocalTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		go.LocalTransform = Transform.Zero;
		Assert.AreEqual( go.LocalTransform, Transform.Zero );

		go.LocalPosition = new Vector3( 10, 10, 10 );
		Assert.AreEqual( go.LocalTransform, Transform.Zero.WithPosition( new Vector3( 10, 10, 10 ) ) );
		Assert.AreEqual( go.LocalPosition, new Vector3( 10, 10, 10 ) );
	}
}
