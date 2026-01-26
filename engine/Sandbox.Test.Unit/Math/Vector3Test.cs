namespace MathTest;

[TestClass]
public class Vector3Test
{
	[TestMethod]
	public void Parse()
	{
		{
			Vector3 v = Vector3.Parse( "1.1,2.1,3.1" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Vector3 v = Vector3.Parse( "1.1, 2.1, 3.1" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Vector3 v = Vector3.Parse( "[1.1,2.1,3.1]" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Vector3 v = Vector3.Parse( "[	1.1,	2.1,	3.1]	" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Vector3 v = Vector3.Parse( "[	1.1,\n\r   2.1,\n\r   3.1\n\r		]	" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}
	}

	[TestMethod]
	public void TryParse()
	{
		{
			Assert.IsTrue( Vector3.TryParse( "1.1,2.1,3.1", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Assert.IsTrue( Vector3.TryParse( "1.1, 2.1, 3.1", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Assert.IsTrue( Vector3.TryParse( "[1.1,2.1,3.1]", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Assert.IsTrue( Vector3.TryParse( "[	1.1,	2.1,	3.1]	", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Assert.IsTrue( Vector3.TryParse( "[	1.1,\n\r   2.1,\n\r   3.1\n\r		]	", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Assert.IsFalse( Vector2.TryParse( "abcdef", out _ ) );
		}
	}

	[TestMethod]
	public void ParseJson()
	{
		{
			Vector3 v = System.Text.Json.JsonSerializer.Deserialize<Vector3>( "\"1.1, 2.1, 3.1\"" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Vector3 v = System.Text.Json.JsonSerializer.Deserialize<Vector3>( "[ 1.1, 2.1, 3.1 ]" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}
	}

	[TestMethod]
	public void AddClamped()
	{
		{
			Vector3 v = new Vector3( 1, 1, 1 );

			v = v.AddClamped( new Vector3( 0, 1, 0 ), 5.0f );
			Assert.AreEqual( v, new Vector3( 1, 2, 1 ) );

			v = v.AddClamped( new Vector3( 0, 2, 0 ), 5.0f );
			Assert.AreEqual( v, new Vector3( 1, 4, 1 ) );

			v = v.AddClamped( new Vector3( 0, 2, 0 ), 5.0f );
			Assert.AreEqual( v, new Vector3( 1, 5, 1 ) );
		}

	}

	[TestMethod]
	public void Negation()
	{
		var v = new Vector3( 1, -2, 3 );
		var neg = -v;

		Assert.AreEqual( -1f, neg.x );
		Assert.AreEqual( 2f, neg.y );
		Assert.AreEqual( -3f, neg.z );

		// Verify double negation returns original
		Assert.AreEqual( v, -(-v) );

		// Zero stays zero
		Assert.AreEqual( Vector3.Zero, -Vector3.Zero );
	}

	[TestMethod]
	public void Sort()
	{
		// Simple case - vectors already in order
		{
			var min = new Vector3( 1, 2, 3 );
			var max = new Vector3( 4, 5, 6 );
			Vector3.Sort( ref min, ref max );

			Assert.AreEqual( new Vector3( 1, 2, 3 ), min );
			Assert.AreEqual( new Vector3( 4, 5, 6 ), max );
		}

		// Reversed vectors
		{
			var min = new Vector3( 4, 5, 6 );
			var max = new Vector3( 1, 2, 3 );
			Vector3.Sort( ref min, ref max );

			Assert.AreEqual( new Vector3( 1, 2, 3 ), min );
			Assert.AreEqual( new Vector3( 4, 5, 6 ), max );
		}

		// Mixed components - each axis sorted independently
		{
			var min = new Vector3( 5, 1, 6 );
			var max = new Vector3( 2, 8, 3 );
			Vector3.Sort( ref min, ref max );

			Assert.AreEqual( new Vector3( 2, 1, 3 ), min );
			Assert.AreEqual( new Vector3( 5, 8, 6 ), max );
		}

		// Negative values
		{
			var min = new Vector3( -1, -5, 3 );
			var max = new Vector3( -3, 2, -1 );
			Vector3.Sort( ref min, ref max );

			Assert.AreEqual( new Vector3( -3, -5, -1 ), min );
			Assert.AreEqual( new Vector3( -1, 2, 3 ), max );
		}
	}

	[TestMethod]
	public void AlmostEqual()
	{
		var v1 = new Vector3( 1, 2, 3 );
		var v2 = new Vector3( 1.00005f, 2.00005f, 3.00005f );
		var v3 = new Vector3( 1.001f, 2.001f, 3.001f );

		// Within default tolerance
		Assert.IsTrue( v1.AlmostEqual( v2 ) );

		// Outside default tolerance
		Assert.IsFalse( v1.AlmostEqual( v3 ) );

		// With custom tolerance
		Assert.IsTrue( v1.AlmostEqual( v3, 0.01f ) );

		// Exact match
		Assert.IsTrue( v1.AlmostEqual( v1 ) );

		// Negative values
		var v4 = new Vector3( -1, -2, -3 );
		var v5 = new Vector3( -1.00005f, -2.00005f, -3.00005f );
		Assert.IsTrue( v4.AlmostEqual( v5 ) );
	}

	[TestMethod]
	public void VectorAngle()
	{
		// Forward direction (1,0,0) should be yaw=0, pitch=0
		{
			var angles = Vector3.VectorAngle( Vector3.Forward );
			Assert.AreEqual( 0f, angles.yaw, 0.001f );
			Assert.AreEqual( 0f, angles.pitch, 0.001f );
		}

		// Straight up (0,0,1) should have pitch of 270
		{
			var angles = Vector3.VectorAngle( Vector3.Up );
			Assert.AreEqual( 270f, angles.pitch, 0.001f );
		}

		// Straight down (0,0,-1) should have pitch of 90
		{
			var angles = Vector3.VectorAngle( Vector3.Down );
			Assert.AreEqual( 90f, angles.pitch, 0.001f );
		}

		// Left direction (0,1,0) should be yaw=90
		{
			var angles = Vector3.VectorAngle( Vector3.Left );
			Assert.AreEqual( 90f, angles.yaw, 0.001f );
		}

		// Right direction (0,-1,0) should be yaw=270
		{
			var angles = Vector3.VectorAngle( Vector3.Right );
			Assert.AreEqual( 270f, angles.yaw, 0.001f );
		}

		// Backward direction (-1,0,0) should be yaw=180
		{
			var angles = Vector3.VectorAngle( Vector3.Backward );
			Assert.AreEqual( 180f, angles.yaw, 0.001f );
		}
	}

	[TestMethod]
	public void ClampLength()
	{
		// Single parameter overload
		{
			var v = new Vector3( 10, 0, 0 );

			// Clamp to shorter length
			var clamped = v.ClampLength( 5 );
			Assert.AreEqual( 5f, clamped.Length, 0.001f );
			Assert.AreEqual( new Vector3( 5, 0, 0 ), clamped );

			// Length already under max - should return same
			var short_v = new Vector3( 3, 0, 0 );
			var notClamped = short_v.ClampLength( 5 );
			Assert.AreEqual( short_v, notClamped );

			// Zero vector should return zero
			var zero = Vector3.Zero.ClampLength( 5 );
			Assert.AreEqual( Vector3.Zero, zero );
		}

		// Diagonal vector
		{
			var v = new Vector3( 10, 10, 10 );
			var originalLength = v.Length;
			var clamped = v.ClampLength( 5 );

			Assert.AreEqual( 5f, clamped.Length, 0.001f );
			// Direction should be preserved
			Assert.IsTrue( v.Normal.AlmostEqual( clamped.Normal ) );
		}

		// Two parameter overload (min, max)
		{
			var v = new Vector3( 10, 0, 0 );

			// Clamp to max
			var clampedMax = v.ClampLength( 2, 5 );
			Assert.AreEqual( 5f, clampedMax.Length, 0.001f );

			// Clamp to min
			var short_v = new Vector3( 1, 0, 0 );
			var clampedMin = short_v.ClampLength( 2, 5 );
			Assert.AreEqual( 2f, clampedMin.Length, 0.001f );

			// Within range - should return same
			var mid = new Vector3( 3, 0, 0 );
			var notClamped = mid.ClampLength( 2, 5 );
			Assert.AreEqual( mid, notClamped );

			// Zero vector should return zero
			var zero = Vector3.Zero.ClampLength( 2, 5 );
			Assert.AreEqual( Vector3.Zero, zero );
		}
	}
}
