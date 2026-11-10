namespace MathTests;

[TestClass]
public class Vector2IntTest
{
	/// <summary>
	/// Constructors should populate the components, including the uniform
	/// single-value form and conversion from Vector3Int.
	/// </summary>
	[TestMethod]
	public void Construct()
	{
		var v = new Vector2Int( 1, 2 );
		Assert.AreEqual( 1, v.x );
		Assert.AreEqual( 2, v.y );

		Assert.AreEqual( new Vector2Int( 3, 3 ), new Vector2Int( 3 ) );
		Assert.AreEqual( new Vector2Int( 4, 5 ), new Vector2Int( new Vector3Int( 4, 5, 6 ) ) );
	}

	/// <summary>
	/// Integer-on-integer arithmetic should stay integral, component-wise.
	/// </summary>
	[TestMethod]
	public void IntegerArithmetic()
	{
		var a = new Vector2Int( 10, 20 );
		var b = new Vector2Int( 3, 4 );

		Assert.AreEqual( new Vector2Int( 13, 24 ), a + b );
		Assert.AreEqual( new Vector2Int( 7, 16 ), a - b );
		Assert.AreEqual( new Vector2Int( 30, 80 ), a * b );
		Assert.AreEqual( new Vector2Int( 3, 5 ), a / b );
		Assert.AreEqual( new Vector2Int( 20, 40 ), a * 2 );
	}

	/// <summary>
	/// Mixing with floats should promote the result to a float vector.
	/// </summary>
	[TestMethod]
	public void FloatArithmeticPromotes()
	{
		var a = new Vector2Int( 10, 20 );

		Vector2 scaled = a * 0.5f;
		Assert.AreEqual( new Vector2( 5, 10 ), scaled );

		Vector2 sum = a + new Vector2( 0.5f, 0.5f );
		Assert.AreEqual( new Vector2( 10.5f, 20.5f ), sum );
	}

	/// <summary>
	/// The indexer should map 0 to x and 1 to y.
	/// </summary>
	[TestMethod]
	public void Indexer()
	{
		var v = new Vector2Int( 7, 8 );

		Assert.AreEqual( 7, v[0] );
		Assert.AreEqual( 8, v[1] );
	}

	/// <summary>
	/// Distance should be the Euclidean distance between the points.
	/// </summary>
	[TestMethod]
	public void Distance()
	{
		var a = new Vector2Int( 0, 0 );
		var b = new Vector2Int( 3, 4 );

		Assert.AreEqual( 5f, a.Distance( b ), 0.0001f );
	}

	/// <summary>
	/// Min and Max should operate per component.
	/// </summary>
	[TestMethod]
	public void MinMax()
	{
		var a = new Vector2Int( 1, 20 );
		var b = new Vector2Int( 10, 2 );

		Assert.AreEqual( new Vector2Int( 1, 2 ), Vector2Int.Min( a, b ) );
		Assert.AreEqual( new Vector2Int( 10, 20 ), Vector2Int.Max( a, b ) );
	}

	/// <summary>
	/// Parse and TryParse should round-trip the ToString format, and TryParse
	/// should reject garbage instead of throwing.
	/// </summary>
	[TestMethod]
	public void ParseRoundTrip()
	{
		var v = new Vector2Int( -3, 14 );

		Assert.AreEqual( v, Vector2Int.Parse( v.ToString() ) );

		Assert.IsTrue( Vector2Int.TryParse( "1,2", System.Globalization.CultureInfo.InvariantCulture, out var parsed ) );
		Assert.AreEqual( new Vector2Int( 1, 2 ), parsed );

		Assert.IsFalse( Vector2Int.TryParse( "abcdef", System.Globalization.CultureInfo.InvariantCulture, out _ ) );
	}

	/// <summary>
	/// Value equality should compare both components.
	/// </summary>
	[TestMethod]
	public void Equality()
	{
		Assert.AreEqual( new Vector2Int( 1, 2 ), new Vector2Int( 1, 2 ) );
		Assert.AreNotEqual( new Vector2Int( 1, 2 ), new Vector2Int( 2, 1 ) );
	}
}
