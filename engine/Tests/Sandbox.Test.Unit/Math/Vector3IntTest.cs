namespace MathTests;

[TestClass]
public class Vector3IntTest
{
	/// <summary>
	/// Constructors should populate the components, including the uniform
	/// single-value form.
	/// </summary>
	[TestMethod]
	public void Construct()
	{
		var v = new Vector3Int( 1, 2, 3 );
		Assert.AreEqual( 1, v.x );
		Assert.AreEqual( 2, v.y );
		Assert.AreEqual( 3, v.z );

		Assert.AreEqual( new Vector3Int( 4, 4, 4 ), new Vector3Int( 4 ) );
	}

	/// <summary>
	/// Integer-on-integer arithmetic should stay integral, component-wise.
	/// </summary>
	[TestMethod]
	public void IntegerArithmetic()
	{
		var a = new Vector3Int( 10, 20, 30 );
		var b = new Vector3Int( 2, 4, 5 );

		Assert.AreEqual( new Vector3Int( 12, 24, 35 ), a + b );
		Assert.AreEqual( new Vector3Int( 8, 16, 25 ), a - b );
		Assert.AreEqual( new Vector3Int( 20, 80, 150 ), a * b );
		Assert.AreEqual( new Vector3Int( 5, 5, 6 ), a / b );
		Assert.AreEqual( new Vector3Int( 20, 40, 60 ), a * 2 );
	}

	/// <summary>
	/// Mixing with floats should promote the result to a float vector.
	/// </summary>
	[TestMethod]
	public void FloatArithmeticPromotes()
	{
		var a = new Vector3Int( 10, 20, 30 );

		Vector3 scaled = a * 0.5f;
		Assert.AreEqual( new Vector3( 5, 10, 15 ), scaled );
	}

	/// <summary>
	/// Mixed Vector2Int arithmetic should leave the z component untouched.
	/// </summary>
	[TestMethod]
	public void Vector2IntArithmetic()
	{
		var a = new Vector3Int( 10, 20, 30 );

		Assert.AreEqual( new Vector3Int( 11, 22, 30 ), a + new Vector2Int( 1, 2 ) );
	}

	/// <summary>
	/// The directional constants should be the engine's z-up, x-forward,
	/// y-left unit axes, and opposites should sum to zero.
	/// </summary>
	[TestMethod]
	public void DirectionConstants()
	{
		Assert.AreEqual( new Vector3Int( 0, 0, 1 ), Vector3Int.Up );
		Assert.AreEqual( new Vector3Int( 0, 0, -1 ), Vector3Int.Down );
		Assert.AreEqual( new Vector3Int( 0, 1, 0 ), Vector3Int.Left );
		Assert.AreEqual( new Vector3Int( 0, -1, 0 ), Vector3Int.Right );
		Assert.AreEqual( new Vector3Int( 1, 0, 0 ), Vector3Int.Forward );
		Assert.AreEqual( new Vector3Int( -1, 0, 0 ), Vector3Int.Backward );

		Assert.AreEqual( Vector3Int.Zero, Vector3Int.Up + Vector3Int.Down );
		Assert.AreEqual( Vector3Int.Zero, Vector3Int.Left + Vector3Int.Right );
		Assert.AreEqual( Vector3Int.Zero, Vector3Int.Forward + Vector3Int.Backward );
	}

	/// <summary>
	/// The normal of an axis-aligned vector should be the unit axis.
	/// </summary>
	[TestMethod]
	public void Normal()
	{
		Assert.AreEqual( new Vector3( 1, 0, 0 ), new Vector3Int( 5, 0, 0 ).Normal );
	}

	/// <summary>
	/// Parse should round-trip the ToString format.
	/// </summary>
	[TestMethod]
	public void ParseRoundTrip()
	{
		var v = new Vector3Int( -3, 14, 7 );

		Assert.AreEqual( v, Vector3Int.Parse( v.ToString() ) );
	}

	/// <summary>
	/// Value equality should compare all three components.
	/// </summary>
	[TestMethod]
	public void Equality()
	{
		Assert.AreEqual( new Vector3Int( 1, 2, 3 ), new Vector3Int( 1, 2, 3 ) );
		Assert.AreNotEqual( new Vector3Int( 1, 2, 3 ), new Vector3Int( 3, 2, 1 ) );
	}
}
