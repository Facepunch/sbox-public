namespace MathTest;

public partial class BBoxTest
{
	[TestMethod]
	public void TraceBBox()
	{
		{
			var bbox = BBox.FromPositionAndSize( 0, 10 );
			var ray = new Ray( new Vector3( -100, 0, 0 ), new Vector3( 1, 0, 0 ) );
			Assert.IsTrue( bbox.Trace( ray, 100, out var dist ) );
			Assert.AreEqual( 95.0f, dist );
		}

		{
			var bbox = BBox.FromPositionAndSize( 0, 10 );
			var ray = new Ray( new Vector3( 100, 0, 0 ), new Vector3( -1, 0, 0 ) );
			Assert.IsTrue( bbox.Trace( ray, 100, out var dist ) );
			Assert.AreEqual( 95.0f, dist );
		}

		{
			var bbox = BBox.FromPositionAndSize( 0, 10 );
			var ray = new Ray( new Vector3( 0, 100, 0 ), new Vector3( 0, -1, 0 ) );
			Assert.IsTrue( bbox.Trace( ray, 100, out var dist ) );
			Assert.AreEqual( 95.0f, dist );
		}

		{
			var bbox = BBox.FromPositionAndSize( 0, 10 );
			var ray = new Ray( new Vector3( 0, -100, 0 ), new Vector3( 0, 1, 0 ) );
			Assert.IsTrue( bbox.Trace( ray, 100, out var dist ) );
			Assert.AreEqual( 95.0f, dist );
		}

		{
			var bbox = BBox.FromPositionAndSize( 0, 10 );
			var ray = new Ray( new Vector3( 0, -100, 0 ), new Vector3( 0, 1, 0 ) );
			Assert.IsFalse( bbox.Trace( ray, 90, out var dist ) );
		}

		{
			var bbox = BBox.FromPositionAndSize( 0, 10 );
			var ray = new Ray( new Vector3( 100, 0, 0 ), new Vector3( -1, 0, 0 ) );
			Assert.IsFalse( bbox.Trace( ray, 90, out var dist ) );
		}

		{
			var bbox = BBox.FromPositionAndSize( 0, 10 );
			var ray = new Ray( new Vector3( 100, 0, 0 ), new Vector3( -1, 0, 0 ) );
			Assert.IsTrue( bbox.Trace( ray, 100000, out var dist ) );
		}

		{
			var bbox = BBox.FromPositionAndSize( 0, 10 );
			var ray = new Ray( new Vector3( 6, 0, 0 ), new Vector3( 1, 0, 0 ) );
			Assert.IsFalse( bbox.Trace( ray, 100000, out var dist ) );
		}
	}

}
