namespace SystemTest;

[TestClass]
public class PlaneTest
{
	[TestMethod]
	public void TraceStraightDown()
	{
		var plane = new Plane( Vector3.Down * 10.0f, Vector3.Up );

		var tr = plane.Trace( new Ray( Vector3.Zero, Vector3.Down ) );

		Assert.IsTrue( tr.HasValue );
		Assert.AreEqual( tr.Value, Vector3.Down * 10.0f );
	}

	[TestMethod]
	public void TraceStraightUp()
	{
		var plane = new Plane( Vector3.Up * 10.0f, Vector3.Down );

		var tr = plane.Trace( new Ray( Vector3.Zero, Vector3.Up ) );

		Assert.IsTrue( tr.HasValue );
		Assert.AreEqual( tr.Value, Vector3.Up * 10.0f );
	}

	[TestMethod]
	public void TraceBackSide()
	{
		var plane = new Plane( Vector3.Down * 10.0f, Vector3.Up );

		var tr = plane.Trace( new Ray( Vector3.Zero, Vector3.Up ) );
		Assert.IsFalse( tr.HasValue );

		tr = plane.Trace( new Ray( Vector3.Zero, Vector3.Right ) );
		Assert.IsFalse( tr.HasValue );

		// Plane is behind ray origin, ray is facing away, it shouldn't hit
		tr = plane.Trace( new Ray( Vector3.Zero, Vector3.Up ), true );
		Assert.IsFalse( tr.HasValue );

		tr = plane.Trace( new Ray( Vector3.One, Vector3.Right ), true );
		Assert.IsFalse( tr.HasValue );
	}
}
