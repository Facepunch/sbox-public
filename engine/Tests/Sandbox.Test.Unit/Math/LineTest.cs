namespace MathTests;

[TestClass]
public class LineTest
{
	/// <summary>
	/// The basic accessors should reflect the two construction points.
	/// </summary>
	[TestMethod]
	public void ConstructAndAccessors()
	{
		var line = new Line( new Vector3( 0, 0, 0 ), new Vector3( 100, 0, 0 ) );

		Assert.AreEqual( new Vector3( 0, 0, 0 ), line.Start );
		Assert.AreEqual( new Vector3( 100, 0, 0 ), line.End );
		Assert.AreEqual( new Vector3( 100, 0, 0 ), line.Delta );
		Assert.AreEqual( new Vector3( 50, 0, 0 ), line.Center );
	}

	/// <summary>
	/// The origin/direction/length constructor should produce the same line as
	/// the two-point constructor.
	/// </summary>
	[TestMethod]
	public void ConstructFromDirection()
	{
		var line = new Line( Vector3.Zero, new Vector3( 1, 0, 0 ), 100 );

		Assert.AreEqual( new Vector3( 100, 0, 0 ), line.End );
	}

	/// <summary>
	/// ClosestPoint should project onto the segment and clamp to the
	/// end points when the query lies beyond them.
	/// </summary>
	[TestMethod]
	public void ClosestPointClampsToSegment()
	{
		var line = new Line( new Vector3( 0, 0, 0 ), new Vector3( 100, 0, 0 ) );

		Assert.AreEqual( new Vector3( 50, 0, 0 ), line.ClosestPoint( new Vector3( 50, 10, 0 ) ) );
		Assert.AreEqual( new Vector3( 0, 0, 0 ), line.ClosestPoint( new Vector3( -50, 10, 0 ) ) );
		Assert.AreEqual( new Vector3( 100, 0, 0 ), line.ClosestPoint( new Vector3( 150, 10, 0 ) ) );
	}

	/// <summary>
	/// Distance should be the distance to the closest point on the segment,
	/// and SqrDistance should be its square.
	/// </summary>
	[TestMethod]
	public void DistanceMatchesClosestPoint()
	{
		var line = new Line( new Vector3( 0, 0, 0 ), new Vector3( 100, 0, 0 ) );
		var query = new Vector3( 50, 10, 0 );

		Assert.AreEqual( 10f, line.Distance( query ), 0.001f );
		Assert.AreEqual( 100f, line.SqrDistance( query ), 0.001f );

		var dist = line.Distance( query, out var closest );
		Assert.AreEqual( 10f, dist, 0.001f );
		Assert.AreEqual( new Vector3( 50, 0, 0 ), closest );
	}

	/// <summary>
	/// A ray crossing through the line within the given radius should trace true,
	/// a distant ray should trace false.
	/// </summary>
	[TestMethod]
	public void Trace()
	{
		var line = new Line( new Vector3( 0, 0, 0 ), new Vector3( 100, 0, 0 ) );

		var crossing = new Ray( new Vector3( 50, -10, 0 ), new Vector3( 0, 1, 0 ) );
		Assert.IsTrue( line.Trace( crossing, 1f ) );

		var missing = new Ray( new Vector3( 50, -10, 50 ), new Vector3( 0, 1, 0 ) );
		Assert.IsFalse( line.Trace( missing, 1f ) );
	}

	/// <summary>
	/// ClosestPoint against a ray should find the mutually closest points
	/// on the segment and on the ray.
	/// </summary>
	[TestMethod]
	public void ClosestPointToRay()
	{
		var line = new Line( new Vector3( 0, 0, 0 ), new Vector3( 100, 0, 0 ) );
		var ray = new Ray( new Vector3( 50, -10, 5 ), new Vector3( 0, 1, 0 ) );

		Assert.IsTrue( line.ClosestPoint( ray, out var onLine, out var onRay ) );
		Assert.AreEqual( new Vector3( 50, 0, 0 ), onLine );
		Assert.AreEqual( new Vector3( 50, 0, 5 ), onRay );
	}

	/// <summary>
	/// Lines with the same end points should be equal.
	/// </summary>
	[TestMethod]
	public void Equality()
	{
		var a = new Line( Vector3.Zero, Vector3.One );
		var b = new Line( Vector3.Zero, Vector3.One );

		Assert.IsTrue( a.Equals( b ) );
	}
}
