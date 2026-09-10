namespace MathTests;

[TestClass]
public class MatrixTest
{
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
