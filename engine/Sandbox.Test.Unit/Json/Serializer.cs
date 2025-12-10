namespace JsonTests;

[TestClass]
public class Serialization
{
	static void RoundTripTest<T>( T obj )
	{
		var json = Json.Serialize( obj );
		var obj_from = Json.Deserialize<T>( json );

		Assert.AreEqual( obj.ToString(), obj_from.ToString() ); // comparing via string to avoid expected accuracy issues
	}

	[TestMethod]
	public void Vector3Serialize()
	{
		RoundTripTest( new Vector3( 0, 0, 0 ) );
		RoundTripTest( new Vector3( 1, 2, 3 ) );
		RoundTripTest( new Vector3( -1, 0, 0 ) );
		RoundTripTest( new Vector3( -10, -10, -10 ) );
		RoundTripTest( new Vector3( 10030.12f, 1000.543f, 1340.1234f ) );
	}

	[TestMethod]
	public void Vector3IntSerialize()
	{
		RoundTripTest( new Vector3Int( 0, 0, 0 ) );
		RoundTripTest( new Vector3Int( 1, 2, 3 ) );
		RoundTripTest( new Vector3Int( -1, 0, 0 ) );
		RoundTripTest( new Vector3Int( -10, -10, -10 ) );
		RoundTripTest( new Vector3Int( 0, -1, 0 ) );
	}

	[TestMethod]
	public void AngleSerialize()
	{
		RoundTripTest( new Angles( 0, 0, 0 ) );
		RoundTripTest( new Angles( 180, 12, 45 ) );
	}

	[TestMethod]
	public void Vector2Serialize()
	{
		RoundTripTest( new Vector2( 0, 0 ) );
		RoundTripTest( new Vector2( 180.3f, 12.234f ) );
		RoundTripTest( new Vector2( -180.3f, 12.234f ) );
		RoundTripTest( new Vector2( -180.3f, -12.234f ) );
		RoundTripTest( new Vector2( -134534680.553f, -13453456.2434f ) );
	}

	[TestMethod]
	public void Vector2IntSerialize()
	{
		RoundTripTest( new Vector2Int( 0, 0 ) );
		RoundTripTest( new Vector2Int( 1, 2 ) );
		RoundTripTest( new Vector2Int( -1, 0 ) );
		RoundTripTest( new Vector2Int( -10, -10 ) );
		RoundTripTest( new Vector2Int( 0, -1 ) );
	}

	[TestMethod]
	public void RotationSerialize()
	{
		RoundTripTest( Rotation.FromAxis( Vector3.Up, 45 ) );
		RoundTripTest( Rotation.FromAxis( Vector3.Up, -45 ) );
		RoundTripTest( Rotation.FromAxis( Vector3.Up + Vector3.Right, -45 ) );
	}

	[TestMethod]
	public void SplinePointSerialize()
	{
		// Test with default values
		RoundTripTest( new Spline.Point
		{
			Position = new Vector3( 0, 0, 0 )
			// Other fields are defaults
		} );

		// Test with all fields set
		RoundTripTest( new Spline.Point
		{
			Position = new Vector3( 1, 2, 3 ),
			In = new Vector3( 0.1f, 0.2f, 0.3f ),
			Out = new Vector3( 0.4f, 0.5f, 0.6f ),
			Mode = Spline.HandleMode.Mirrored,
			Roll = 45f,
			Scale = new Vector3( 1.5f, 2f, 2f ),
			Up = new Vector3( 0, 1, 0 )
		} );

		// Test with some fields missing (testing defaults on deserialization)
		RoundTripTest( new Spline.Point
		{
			Position = new Vector3( -1, -2, -3 ),
			// InPositionRelative and OutPositionRelative are defaults
			Mode = Spline.HandleMode.Linear,
			// Roll is default
			Scale = new Vector3( 0.5f, 0.5f, 0.5f ),
			// UpVector is default
		} );
	}
}
