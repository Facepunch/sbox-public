namespace SystemTest;

[TestClass]
public class TransformTest
{
	[TestInitialize]
	public void SeedRandom()
	{
		SandboxSystem.SetRandomSeed( 0x1d655be6 );
	}

	[TestMethod]
	public void ToLocalToWorld()
	{
		var parent = new Transform( SandboxSystem.Random.VectorInCube() * 100, Rotation.LookAt( SandboxSystem.Random.VectorInCube() ) );
		var childWorld = new Transform( SandboxSystem.Random.VectorInCube() * 100, Rotation.LookAt( SandboxSystem.Random.VectorInCube() ) );

		var childLocal = parent.ToLocal( childWorld );

		var childWorldTest = parent.ToWorld( childLocal );

		Assert.IsTrue( (childWorldTest.Position - childWorld.Position).Length < 0.0001f );
		Assert.AreEqual( childWorldTest.Rotation, childWorld.Rotation );
		Assert.AreEqual( childWorldTest.Scale, childWorld.Scale );
	}

	[TestMethod]
	public void DefaultConstructorScaleIsOne()
	{
		var tx = new Transform();
		Assert.IsTrue( tx.Scale == 1f );
	}

	[TestMethod]
	public void ToLocalToWorld_UniformScale()
	{
		var parent = new Transform( SandboxSystem.Random.VectorInCube() * 100, Rotation.LookAt( SandboxSystem.Random.VectorInCube() ), 2 );
		var childWorld = new Transform( SandboxSystem.Random.VectorInCube() * 100, Rotation.LookAt( SandboxSystem.Random.VectorInCube() ), 1 );

		var childLocal = parent.ToLocal( childWorld );

		var childWorldTest = parent.ToWorld( childLocal );

		Assert.IsTrue( (childWorldTest.Position - childWorld.Position).Length < 0.0001f );
		Assert.AreEqual( childWorldTest.Rotation, childWorld.Rotation );
		Assert.AreEqual( childWorldTest.Scale, childWorld.Scale );
	}

	[TestMethod]
	public void ToLocalToWorld_Scale()
	{
		var parent = new Transform( SandboxSystem.Random.VectorInCube() * 100, Rotation.LookAt( SandboxSystem.Random.VectorInCube() ), new Vector3( 1, 0.2f, 0.2f ) );
		var childWorld = new Transform( SandboxSystem.Random.VectorInCube() * 100, Rotation.LookAt( SandboxSystem.Random.VectorInCube() ) );

		var childLocal = parent.ToLocal( childWorld );

		var childWorldTest = parent.ToWorld( childLocal );

		Assert.AreEqual( childWorldTest.Rotation, childWorld.Rotation );
		Assert.AreEqual( childWorldTest.Scale, childWorld.Scale );
		Assert.IsTrue( (childWorldTest.Position - childWorld.Position).Length < 0.001f, $"{(childWorldTest.Position - childWorld.Position).Length}" );
	}

	[TestMethod]
	[DataRow( 1.0f, 1.0f )]
	[DataRow( 2.0f, 1.0f )]
	[DataRow( 3.0f, 1.0f )]
	[DataRow( 3.0f, 2.0f )]
	[DataRow( 3.0f, 3.0f )]
	[DataRow( 2.0f, 3.0f )]
	[DataRow( 1.0f, 3.0f )]
	[DataRow( 0.5f, 3.0f )]
	[DataRow( 0.5f, 2.0f )]
	[DataRow( 0.5f, 1.0f )]
	[DataRow( 0.5f, 0.5f )]
	public void ToLocalToWorldWithScale( float rootScale, float childScale )
	{
		var parent = new Transform( SandboxSystem.Random.VectorInCube() * 100, Rotation.LookAt( SandboxSystem.Random.VectorInCube() ), rootScale );
		var childWorld = new Transform( SandboxSystem.Random.VectorInCube() * 100, Rotation.LookAt( SandboxSystem.Random.VectorInCube() ), childScale );

		var childLocal = parent.ToLocal( childWorld );

		var childWorldTest = parent.ToWorld( childLocal );

		Assert.IsTrue( childWorldTest.Position == childWorld.Position );
		Assert.IsTrue( childWorldTest.Rotation == childWorld.Rotation );
		Assert.AreEqual( childWorldTest.Scale, childWorld.Scale );
	}

	[TestMethod]
	public void PointToLocalWorld()
	{
		var point = SandboxSystem.Random.VectorInCube() * 100;
		var parent = new Transform( SandboxSystem.Random.VectorInCube() * 100, Rotation.LookAt( SandboxSystem.Random.VectorInCube() ) );

		var lp = parent.PointToLocal( point );
		var wp = parent.PointToWorld( lp );

		Assert.IsFalse( point == lp );
		Assert.IsTrue( wp == point );
	}

	[TestMethod]
	public void PointToLocalWorld_WithScale()
	{
		var point = new Vector3( 100, 100, 100 );
		var parent = new Transform( new Vector3( 1000, 1000, 1000 ), new Angles( 45, 0, 0 ), new Vector3( 1, 0.5f, 0.25f ) );

		var lp = parent.PointToLocal( point );

		System.Console.WriteLine( $"To Local: {point} => {lp}" );

		var wp = parent.PointToWorld( lp );

		System.Console.WriteLine( $"To World: {lp} => {wp}" );

		Assert.IsFalse( point == lp );
		Assert.IsTrue( point.AlmostEqual( wp, 0.001f ), $"{point} doesn't equal {wp}" );
	}

	[TestMethod]
	public void PointToWorld_ToWorld()
	{
		var parent = new Transform(
			SandboxSystem.Random.VectorInCube() * 100,
			Rotation.LookAt( SandboxSystem.Random.VectorInCube() ),
			new Vector3( 1, 2, 0.5f )
		);

		var point = SandboxSystem.Random.VectorInCube() * 100;
		var pointWorld = parent.PointToWorld( point );
		var transformWorld = parent.ToWorld( new Transform( point ) ).Position;

		Assert.IsTrue( pointWorld.AlmostEqual( transformWorld, 0.001f ), $"{pointWorld} does not match ToWorld result: {transformWorld}" );
	}

	[TestMethod]
	public void PointToLocal_ToLocal()
	{
		var parent = new Transform(
			SandboxSystem.Random.VectorInCube() * 100,
			Rotation.LookAt( SandboxSystem.Random.VectorInCube() ),
			new Vector3( 2, 1, 0.5f )
		);

		var point = SandboxSystem.Random.VectorInCube() * 100;
		var pointLocal = parent.PointToLocal( point );
		var transformLocal = parent.ToLocal( new Transform( point ) ).Position;

		Assert.IsTrue( pointLocal.AlmostEqual( transformLocal, 0.001f ), $"{pointLocal} does not match ToLocal result: {transformLocal}" );
	}
}
