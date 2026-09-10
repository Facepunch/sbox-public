namespace MathTests;

[TestClass]
public class RectTest
{
	/// <summary>
	/// Constructing from position and size should expose the same values through
	/// the edge properties and Size/Position accessors.
	/// </summary>
	[TestMethod]
	public void ConstructAndAccessors()
	{
		var r = new Rect( 10, 20, 30, 40 );

		Assert.AreEqual( 10, r.Left );
		Assert.AreEqual( 20, r.Top );
		Assert.AreEqual( 40, r.Right );
		Assert.AreEqual( 60, r.Bottom );
		Assert.AreEqual( 30, r.Width );
		Assert.AreEqual( 40, r.Height );
		Assert.AreEqual( new Vector2( 10, 20 ), r.Position );
		Assert.AreEqual( new Vector2( 30, 40 ), r.Size );
		Assert.AreEqual( new Vector2( 25, 40 ), r.Center );
	}

	/// <summary>
	/// The corner accessors should combine the edges in the expected order.
	/// </summary>
	[TestMethod]
	public void Corners()
	{
		var r = new Rect( 1, 2, 3, 4 );

		Assert.AreEqual( new Vector2( 1, 2 ), r.TopLeft );
		Assert.AreEqual( new Vector2( 4, 2 ), r.TopRight );
		Assert.AreEqual( new Vector2( 1, 6 ), r.BottomLeft );
		Assert.AreEqual( new Vector2( 4, 6 ), r.BottomRight );
	}

	/// <summary>
	/// FromPoints should produce the same rect regardless of which corner
	/// is passed first.
	/// </summary>
	[TestMethod]
	public void FromPoints()
	{
		var a = Rect.FromPoints( new Vector2( 0, 0 ), new Vector2( 10, 20 ) );
		var b = Rect.FromPoints( new Vector2( 10, 20 ), new Vector2( 0, 0 ) );

		Assert.AreEqual( a, b );
		Assert.AreEqual( 10, a.Width );
		Assert.AreEqual( 20, a.Height );
	}

	/// <summary>
	/// Point containment should be true inside and false outside.
	/// </summary>
	[TestMethod]
	public void IsInsidePoint()
	{
		var r = new Rect( 0, 0, 100, 100 );

		Assert.IsTrue( r.IsInside( new Vector2( 50, 50 ) ) );
		Assert.IsFalse( r.IsInside( new Vector2( 150, 50 ) ) );
		Assert.IsFalse( r.IsInside( new Vector2( 50, -1 ) ) );
	}

	/// <summary>
	/// Rect containment: overlapping rects count as inside unless fullyInside
	/// is requested, in which case the candidate must be enclosed completely.
	/// </summary>
	[TestMethod]
	public void IsInsideRect()
	{
		var outer = new Rect( 0, 0, 100, 100 );
		var contained = new Rect( 25, 25, 50, 50 );
		var overlapping = new Rect( 75, 75, 50, 50 );
		var separate = new Rect( 200, 200, 10, 10 );

		Assert.IsTrue( outer.IsInside( contained ) );
		Assert.IsTrue( outer.IsInside( contained, fullyInside: true ) );

		Assert.IsTrue( outer.IsInside( overlapping ) );
		Assert.IsFalse( outer.IsInside( overlapping, fullyInside: true ) );

		Assert.IsFalse( outer.IsInside( separate ) );
	}

	/// <summary>
	/// Shrink should pull each edge inwards and Grow should push it back out -
	/// growing by the same amount should restore the original rect.
	/// </summary>
	[TestMethod]
	public void ShrinkGrowRoundTrip()
	{
		var r = new Rect( 0, 0, 100, 100 );

		var shrunk = r.Shrink( 10 );
		Assert.AreEqual( new Rect( 10, 10, 80, 80 ), shrunk );

		Assert.AreEqual( r, shrunk.Grow( 10 ) );
	}

	/// <summary>
	/// Adding a point outside the rect should expand it to contain that point,
	/// leaving the other edges untouched.
	/// </summary>
	[TestMethod]
	public void AddPointExpands()
	{
		var r = new Rect( 0, 0, 10, 10 );
		var expanded = r.AddPoint( new Vector2( 20, 5 ) );

		Assert.AreEqual( 0, expanded.Left );
		Assert.AreEqual( 20, expanded.Right );
		Assert.AreEqual( 0, expanded.Top );
		Assert.AreEqual( 10, expanded.Bottom );

		Assert.IsTrue( expanded.IsInside( new Vector2( 19, 5 ) ) );
	}

	/// <summary>
	/// Adding a rect should produce the union of both rects.
	/// </summary>
	[TestMethod]
	public void AddRectIsUnion()
	{
		var r = new Rect( 0, 0, 10, 10 );
		r.Add( new Rect( 20, 20, 10, 10 ) );

		Assert.AreEqual( 0, r.Left );
		Assert.AreEqual( 0, r.Top );
		Assert.AreEqual( 30, r.Right );
		Assert.AreEqual( 30, r.Bottom );
	}

	/// <summary>
	/// ClosestPoint should return the point itself when inside, and clamp
	/// to the nearest edge when outside.
	/// </summary>
	[TestMethod]
	public void ClosestPoint()
	{
		var r = new Rect( 0, 0, 100, 100 );

		Assert.AreEqual( new Vector2( 50, 50 ), r.ClosestPoint( new Vector2( 50, 50 ) ) );
		Assert.AreEqual( new Vector2( 100, 50 ), r.ClosestPoint( new Vector2( 150, 50 ) ) );
		Assert.AreEqual( new Vector2( 0, 0 ), r.ClosestPoint( new Vector2( -10, -10 ) ) );
	}

	/// <summary>
	/// Offsetting by a vector should move the position without changing the size.
	/// </summary>
	[TestMethod]
	public void OffsetOperator()
	{
		var r = new Rect( 0, 0, 10, 10 ) + new Vector2( 5, 6 );

		Assert.AreEqual( new Vector2( 5, 6 ), r.Position );
		Assert.AreEqual( new Vector2( 10, 10 ), r.Size );
	}

	/// <summary>
	/// WithoutPosition should keep the size but zero the position.
	/// </summary>
	[TestMethod]
	public void WithoutPosition()
	{
		var r = new Rect( 5, 6, 10, 20 ).WithoutPosition;

		Assert.AreEqual( Vector2.Zero, r.Position );
		Assert.AreEqual( new Vector2( 10, 20 ), r.Size );
	}

	/// <summary>
	/// Floor, Round and Ceiling should snap fractional edges in the
	/// expected directions.
	/// </summary>
	[TestMethod]
	public void FloorRoundCeiling()
	{
		var r = new Rect( 0.4f, 0.6f, 10, 10 );

		Assert.AreEqual( 0, r.Floor().Left );
		Assert.AreEqual( 0, r.Round().Left );
		Assert.AreEqual( 1, r.Round().Top );
		Assert.AreEqual( 1, r.Ceiling().Left );
	}

	/// <summary>
	/// Value equality should compare all four edges; operators should agree.
	/// </summary>
	[TestMethod]
	public void Equality()
	{
		var a = new Rect( 1, 2, 3, 4 );
		var b = new Rect( 1, 2, 3, 4 );
		var c = new Rect( 1, 2, 3, 5 );

		Assert.IsTrue( a == b );
		Assert.IsTrue( a != c );
		Assert.AreEqual( a.GetHashCode(), b.GetHashCode() );
	}
}
