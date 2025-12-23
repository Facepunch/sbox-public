namespace SystemTest;

[TestClass]
public class SphereTest
{
	[TestMethod]
	public void TraceSphere()
	{
		{
			var sphere = new Sphere( 0, 5 );
			var ray = new Ray( new Vector3( -100, 0, 0 ), new Vector3( 1, 0, 0 ) );
			Assert.IsTrue( sphere.Trace( ray, 100, out var dist ) );
			Assert.AreEqual( 95.0f, dist );
		}

		{
			var sphere = new Sphere( 0, 5 );
			var ray = new Ray( new Vector3( 100, 0, 0 ), new Vector3( -1, 0, 0 ) );
			Assert.IsTrue( sphere.Trace( ray, 100, out var dist ) );
			Assert.AreEqual( 95.0f, dist );
		}

		{
			var sphere = new Sphere( 0, 95 );
			var ray = new Ray( new Vector3( 100, 0, 0 ), new Vector3( -1, 0, 0 ) );
			Assert.IsTrue( sphere.Trace( ray, 100, out var dist ) );
			Assert.AreEqual( 5.0f, dist );
		}

		{
			var sphere = new Sphere( 0, 10 );
			var ray = new Ray( new Vector3( 100, 11, 0 ), new Vector3( -1, 0, 0 ) );
			Assert.IsFalse( sphere.Trace( ray, float.MaxValue, out var dist ) );
		}

	}

	[TestMethod]
	public void SerializeSphere()
	{
		var sphere = new Sphere( new Vector3( 1, 2, 3 ), 32 );

		var json = Json.Serialize( sphere );

		var newSphere = Json.Deserialize<Sphere>( json );

		Assert.AreEqual( sphere.Radius, newSphere.Radius );
		Assert.AreEqual( sphere.Center, newSphere.Center );
	}

}
