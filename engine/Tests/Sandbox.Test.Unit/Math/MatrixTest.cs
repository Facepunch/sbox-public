namespace MathTests;

[TestClass]
public class MatrixTest
{
	[TestMethod]
	public void EqualityMatchesNumerics()
	{
		float[] values = [ 0, System.BitConverter.Int32BitsToSingle( int.MinValue ), 1, -1, float.BitIncrement( 1 ),
			float.Epsilon, float.MaxValue, float.PositiveInfinity, float.NegativeInfinity, float.NaN,
			System.BitConverter.Int32BitsToSingle( 0x7FC00001 ) ];

		for ( int element = 0; element < 16; element++ )
		{
			foreach ( var left in values )
			{
				foreach ( var right in values )
				{
					var a = System.Numerics.Matrix4x4.Identity;
					var b = a;
					a[element / 4, element % 4] = left;
					b[element / 4, element % 4] = right;
					Matrix matrixA = a;
					Matrix matrixB = b;
					var expected = a == b;

					Assert.AreEqual( expected, matrixA == matrixB );
					Assert.AreEqual( !expected, matrixA != matrixB );
					Assert.AreEqual( expected, matrixA.Equals( matrixB ) );
					Assert.AreEqual( expected, matrixA.Equals( (object)matrixB ) );
				}
			}
		}
	}

	[TestMethod]
	public void SignedZerosHaveEqualHashes()
	{
		Matrix positive = default;
		var negative = new System.Numerics.Matrix4x4();
		for ( int row = 0; row < 4; row++ )
		{
			for ( int column = 0; column < 4; column++ )
			{
				negative[row, column] = System.BitConverter.Int32BitsToSingle( int.MinValue );
			}
		}

		Matrix other = negative;
		Assert.IsTrue( positive == other );
		Assert.AreEqual( positive.GetHashCode(), other.GetHashCode() );
	}

	/// <summary>
	/// Matrix.FromTransform should build a matrix that maps local points to the
	/// same world positions the Transform itself produces.
	/// </summary>
	[TestMethod]
	public void FromTransform()
	{
		var transform = new Transform(
			new Vector3( 100, 420, 340 ),
			Rotation.From( 90, 0, 45 ),
			2.0f
		);

		var mat = Matrix.FromTransform( transform );

		var points = new[]
		{
			Vector3.Zero,
			new Vector3( 1, 0, 0 ),
			new Vector3( -5, 3, 12 )
		};

		foreach ( var point in points )
		{
			var expected = transform.PointToWorld( point );
			var actual = mat.Transform( point );

			Assert.IsTrue( expected.AlmostEqual( actual, 0.01f ), $"{point}: expected {expected}, got {actual}" );
		}
	}

	[TestMethod]
	public void ToTransform()
	{
		var transform = new Transform(
			new Vector3( 100, 420, 340 ),
			Rotation.From( 90, 0, 45 ),
			2.0f
		);

		var mat = Matrix.FromTransform( transform );
		var tx = mat.ExtractTransform();

		Assert.IsTrue( transform.AlmostEqual( tx ) );
	}
}
