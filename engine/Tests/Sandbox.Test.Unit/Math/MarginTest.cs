using Sandbox.UI;

namespace MathTests;

[TestClass]
public class MarginTest
{
	/// <summary>
	/// The constructors should fan a uniform value, a horizontal/vertical
	/// pair, or four explicit edges out to the edge fields.
	/// </summary>
	[TestMethod]
	public void Construct()
	{
		var uniform = new Margin( 5 );
		Assert.AreEqual( 5, uniform.Left );
		Assert.AreEqual( 5, uniform.Top );
		Assert.AreEqual( 5, uniform.Right );
		Assert.AreEqual( 5, uniform.Bottom );

		var pair = new Margin( 2, 3 );
		Assert.AreEqual( 2, pair.Left );
		Assert.AreEqual( 3, pair.Top );
		Assert.AreEqual( 2, pair.Right );
		Assert.AreEqual( 3, pair.Bottom );

		var full = new Margin( 1, 2, 3, 4 );
		Assert.AreEqual( 1, full.Left );
		Assert.AreEqual( 2, full.Top );
		Assert.AreEqual( 3, full.Right );
		Assert.AreEqual( 4, full.Bottom );
	}

	/// <summary>
	/// EdgeSize should be the sum of the opposing edges per axis.
	/// </summary>
	[TestMethod]
	public void EdgeSize()
	{
		var m = new Margin( 1, 2, 3, 4 );

		Assert.AreEqual( new Vector2( 4, 6 ), m.EdgeSize );
	}

	/// <summary>
	/// EdgeAdd treats the margin as rect edges: insetting adds to left/top but
	/// subtracts from right/bottom. EdgeSubtract should be its inverse.
	/// </summary>
	[TestMethod]
	public void EdgeAddSubtractRoundTrip()
	{
		var m = new Margin( 10, 10, 10, 10 );
		var delta = new Margin( 1, 2, 3, 4 );

		var added = m.EdgeAdd( delta );
		Assert.AreEqual( 11, added.Left );
		Assert.AreEqual( 12, added.Top );
		Assert.AreEqual( 7, added.Right );
		Assert.AreEqual( 6, added.Bottom );

		var back = added.EdgeSubtract( delta );
		Assert.AreEqual( m.Left, back.Left );
		Assert.AreEqual( m.Top, back.Top );
		Assert.AreEqual( m.Right, back.Right );
		Assert.AreEqual( m.Bottom, back.Bottom );
	}

	/// <summary>
	/// IsNearlyZero should be true only when all edges are within tolerance.
	/// </summary>
	[TestMethod]
	public void IsNearlyZero()
	{
		Assert.IsTrue( default( Margin ).IsNearlyZero() );
		Assert.IsTrue( new Margin( 0.0000001f ).IsNearlyZero() );
		Assert.IsFalse( new Margin( 1 ).IsNearlyZero() );
	}

	/// <summary>
	/// The arithmetic operators should apply per edge.
	/// </summary>
	[TestMethod]
	public void Operators()
	{
		var sum = new Margin( 1, 2, 3, 4 ) + new Margin( 10, 20, 30, 40 );
		Assert.AreEqual( 11, sum.Left );
		Assert.AreEqual( 44, sum.Bottom );

		var scaled = new Margin( 1, 2, 3, 4 ) * 2;
		Assert.AreEqual( 2, scaled.Left );
		Assert.AreEqual( 8, scaled.Bottom );
	}
}
