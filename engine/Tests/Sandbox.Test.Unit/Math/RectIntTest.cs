namespace MathTests;

[TestClass]
public class RectIntTest
{
	/// <summary>
	/// Constructing from position and size should expose the same values through
	/// the edge properties and Size/Position accessors.
	/// </summary>
	[TestMethod]
	public void ConstructAndAccessors()
	{
		var r = new RectInt( 10, 20, 30, 40 );

		Assert.AreEqual( 10, r.Left );
		Assert.AreEqual( 20, r.Top );
		Assert.AreEqual( 40, r.Right );
		Assert.AreEqual( 60, r.Bottom );
		Assert.AreEqual( 30, r.Width );
		Assert.AreEqual( 40, r.Height );
		Assert.AreEqual( new Vector2Int( 10, 20 ), r.Position );
		Assert.AreEqual( new Vector2Int( 30, 40 ), r.Size );
	}

	/// <summary>
	/// FromPoints should produce the same rect regardless of which corner
	/// is passed first.
	/// </summary>
	[TestMethod]
	public void FromPoints()
	{
		var a = RectInt.FromPoints( new Vector2Int( 0, 0 ), new Vector2Int( 10, 20 ) );
		var b = RectInt.FromPoints( new Vector2Int( 10, 20 ), new Vector2Int( 0, 0 ) );

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
		var r = new RectInt( 0, 0, 100, 100 );

		Assert.IsTrue( r.IsInside( new Vector2Int( 50, 50 ) ) );
		Assert.IsFalse( r.IsInside( new Vector2Int( 150, 50 ) ) );
		Assert.IsFalse( r.IsInside( new Vector2Int( 50, -1 ) ) );
	}

	/// <summary>
	/// Rect containment: overlap counts as inside unless fullyInside is
	/// requested, in which case the candidate must be enclosed completely.
	/// </summary>
	[TestMethod]
	public void IsInsideRect()
	{
		var outer = new RectInt( 0, 0, 100, 100 );
		var contained = new RectInt( 25, 25, 50, 50 );
		var overlapping = new RectInt( 75, 75, 50, 50 );
		var separate = new RectInt( 200, 200, 10, 10 );

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
		var r = new RectInt( 0, 0, 100, 100 );

		var shrunk = r.Shrink( 10 );
		Assert.AreEqual( new RectInt( 10, 10, 80, 80 ), shrunk );

		Assert.AreEqual( r, shrunk.Grow( 10 ) );
	}

	/// <summary>
	/// Adding a point outside the rect should expand it to contain that point.
	/// </summary>
	[TestMethod]
	public void AddPointExpands()
	{
		var r = new RectInt( 0, 0, 10, 10 );
		var expanded = r.AddPoint( new Vector2Int( 20, 5 ) );

		Assert.AreEqual( 0, expanded.Left );
		Assert.AreEqual( 20, expanded.Right );
		Assert.AreEqual( 10, expanded.Bottom );
	}

	/// <summary>
	/// Adding a rect should produce the union of both rects.
	/// </summary>
	[TestMethod]
	public void AddRectIsUnion()
	{
		var r = new RectInt( 0, 0, 10, 10 );
		r.Add( new RectInt( 20, 20, 10, 10 ) );

		Assert.AreEqual( 0, r.Left );
		Assert.AreEqual( 30, r.Right );
		Assert.AreEqual( 30, r.Bottom );
	}

	/// <summary>
	/// Offsetting by a vector should move the position without changing the size.
	/// </summary>
	[TestMethod]
	public void OffsetOperator()
	{
		var r = new RectInt( 0, 0, 10, 10 ) + new Vector2Int( 5, 6 );

		Assert.AreEqual( new Vector2Int( 5, 6 ), r.Position );
		Assert.AreEqual( new Vector2Int( 10, 10 ), r.Size );
	}

	/// <summary>
	/// Value equality should compare all four edges; operators should agree.
	/// </summary>
	[TestMethod]
	public void Equality()
	{
		var a = new RectInt( 1, 2, 3, 4 );
		var b = new RectInt( 1, 2, 3, 4 );
		var c = new RectInt( 1, 2, 3, 5 );

		Assert.IsTrue( a == b );
		Assert.IsTrue( a != c );
		Assert.AreEqual( a.GetHashCode(), b.GetHashCode() );
	}
}
