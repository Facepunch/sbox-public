namespace MathTests;

[TestClass]
public class ConeTest
{
	// Base of radius 10 at the origin, tapering to a point 100 up.
	static readonly Cone TaperedCone = new( Vector3.Zero, new Vector3( 0, 0, 100 ), 10, 0 );

	/// <summary>
	/// Points on the axis inside the cone should be contained; points beyond
	/// the slanted surface or past the ends should not.
	/// </summary>
	[TestMethod]
	public void Contains()
	{
		Assert.IsTrue( TaperedCone.Contains( new Vector3( 0, 0, 1 ) ) );
		Assert.IsTrue( TaperedCone.Contains( new Vector3( 4, 0, 10 ) ) );

		Assert.IsFalse( TaperedCone.Contains( new Vector3( 9, 0, 90 ) ) );
		Assert.IsFalse( TaperedCone.Contains( new Vector3( 0, 0, 150 ) ) );
		Assert.IsFalse( TaperedCone.Contains( new Vector3( 0, 0, -10 ) ) );
	}

	/// <summary>
	/// Bounds should fully enclose the base circle and the tip.
	/// </summary>
	[TestMethod]
	public void Bounds()
	{
		var bounds = TaperedCone.Bounds;

		Assert.IsTrue( bounds.Mins.z <= 0 );
		Assert.IsTrue( bounds.Maxs.z >= 100 );
		Assert.IsTrue( bounds.Mins.x <= -10 );
		Assert.IsTrue( bounds.Maxs.x >= 10 );
	}

	/// <summary>
	/// Sampled random points must always satisfy Contains.
	/// </summary>
	[TestMethod]
	public void RandomPointInsideIsContained()
	{
		for ( int i = 0; i < 100; i++ )
		{
			var p = TaperedCone.RandomPointInside;
			Assert.IsTrue( TaperedCone.Contains( p ), $"{p} not inside cone" );
		}
	}

	/// <summary>
	/// Edge distance should grow as the query point moves away from the surface.
	/// </summary>
	[TestMethod]
	public void EdgeDistanceOrdering()
	{
		var near = TaperedCone.GetEdgeDistance( new Vector3( 15, 0, 0 ) );
		var far = TaperedCone.GetEdgeDistance( new Vector3( 50, 0, 0 ) );

		Assert.IsTrue( near < far );
	}

	/// <summary>
	/// Cones with identical centers and radii should be equal.
	/// </summary>
	[TestMethod]
	public void Equality()
	{
		var a = new Cone( Vector3.Zero, Vector3.Up, 5, 1 );
		var b = new Cone( Vector3.Zero, Vector3.Up, 5, 1 );
		var c = new Cone( Vector3.Zero, Vector3.Up, 5, 2 );

		Assert.IsTrue( a == b );
		Assert.IsTrue( a != c );
	}
}
