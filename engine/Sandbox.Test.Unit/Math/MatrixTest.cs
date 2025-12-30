namespace MathTest;

[TestClass]
public class MatrixTest
{
	[TestMethod]
	public void FromTransform()
	{
		var transform = new Transform(
			new Vector3( 100, 420, 340 ),
			Rotation.From( 90, 0, 45 ),
			2.0f
		);

		var mat = Matrix.FromTransform( transform );

		var expectedScale = Matrix.CreateScale( transform.Scale );
		var expectedRotation = Matrix.CreateRotation( transform.Rotation );
		var expectedTranslation = Matrix.CreateTranslation( transform.Position );
		var expectedMatrix = expectedScale * expectedRotation * expectedTranslation;

		Assert.AreEqual( mat, expectedMatrix );
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

		Assert.AreEqual( transform, tx );
	}
}
