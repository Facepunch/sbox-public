namespace MathTest;

public partial class BBoxTest
{
	[TestMethod]
	public void IdentityTransform()
	{
		var bbox = new BBox( new Vector3( -10, -20, -30 ), new Vector3( 10, 20, 30 ) );

		// Apply identity transform
		var transform = Transform.Zero;
		var transformedBBox = bbox.Transform( transform );

		// Should be the same bbox
		Assert.IsTrue( bbox.Mins.AlmostEqual( transformedBBox.Mins ) );
		Assert.IsTrue( bbox.Maxs.AlmostEqual( transformedBBox.Maxs ) );
	}

	[TestMethod]
	public void TranslationOnly()
	{
		var bbox = new BBox( new Vector3( -10, -20, -30 ), new Vector3( 10, 20, 30 ) );

		// Apply translation
		var transform = new Transform( new Vector3( 100, 200, 300 ) );
		var transformedBBox = bbox.Transform( transform );

		// Should be translated
		Assert.IsTrue( new Vector3( 90, 180, 270 ).AlmostEqual( transformedBBox.Mins ) );
		Assert.IsTrue( new Vector3( 110, 220, 330 ).AlmostEqual( transformedBBox.Maxs ) );
	}

	[TestMethod]
	public void UniformScale()
	{
		var bbox = new BBox( new Vector3( -10, -20, -30 ), new Vector3( 10, 20, 30 ) );

		// Apply uniform scale
		var transform = new Transform();
		transform.Scale = new Vector3( 2, 2, 2 );
		var transformedBBox = bbox.Transform( transform );

		// Should be scaled from center
		Assert.IsTrue( new Vector3( -20, -40, -60 ).AlmostEqual( transformedBBox.Mins ) );
		Assert.IsTrue( new Vector3( 20, 40, 60 ).AlmostEqual( transformedBBox.Maxs ) );
	}

	[TestMethod]
	public void NonUniformScale()
	{
		var bbox = new BBox( new Vector3( -10, -20, -30 ), new Vector3( 10, 20, 30 ) );

		// Apply non-uniform scale
		var transform = new Transform();
		transform.Scale = new Vector3( 2, 3, 4 );
		var transformedBBox = bbox.Transform( transform );

		// Should be scaled differently on each axis
		Assert.IsTrue( new Vector3( -20, -60, -120 ).AlmostEqual( transformedBBox.Mins ) );
		Assert.IsTrue( new Vector3( 20, 60, 120 ).AlmostEqual( transformedBBox.Maxs ) );
	}

	[TestMethod]
	public void RotationOnly()
	{
		var bbox = new BBox( new Vector3( -10, -10, -10 ), new Vector3( 10, 10, 10 ) );

		// Apply 90 degree rotation around Y axis
		var transform = new Transform( Vector3.Zero, Rotation.FromYaw( 90 ) );
		var transformedBBox = bbox.Transform( transform );

		// For a cube, 90 degree rotation should yield the same bounds
		Assert.IsTrue( new Vector3( -10, -10, -10 ).AlmostEqual( transformedBBox.Mins ) );
		Assert.IsTrue( new Vector3( 10, 10, 10 ).AlmostEqual( transformedBBox.Maxs ) );
	}

	[TestMethod]
	public void AsymmetricBox()
	{
		var bbox = new BBox( new Vector3( -5, -10, -20 ), new Vector3( 15, 10, 5 ) );

		Assert.IsTrue( bbox.Center.AlmostEqual( new Vector3( 5f, 0f, -7.5f ) ) );
		Assert.IsTrue( bbox.Extents.AlmostEqual( new Vector3( 10f, 10f, 12.5f ) ) );

		// Apply uniform scale
		var transform = new Transform();
		transform.Scale = new Vector3( 2, 2, 2 );
		var transformedBBox = bbox.Transform( transform );

		// Test that all original corners, when transformed individually, are contained in the transformed bbox
		foreach ( var corner in bbox.Corners )
		{
			var transformedCorner = transform.PointToWorld( corner );
			Assert.IsTrue( transformedBBox.Contains( transformedCorner ),
				$"Corner {corner} transformed to {transformedCorner} is not contained in the bbox {transformedBBox}" );
		}

		var outsidePoints = new[]
		{
			new Vector3( -6, 0, -7.5f ),     // Beyond min X (using center Z)
			new Vector3( 16, 0, -7.5f ),     // Beyond max X (using center Z)
			new Vector3( 5, -11, -7.5f ),    // Beyond min Y (using center X,Z)
			new Vector3( 5, 11, -7.5f ),     // Beyond max Y (using center X,Z)
			new Vector3( 5, 0, -21 ),        // Beyond min Z (using center X,Y)
			new Vector3( 5, 0, 6 )           // Beyond max Z (using center X,Y)
		};

		foreach ( var point in outsidePoints )
		{
			// Verify point is outside original box
			Assert.IsFalse( bbox.Contains( point ),
				$"Test point {point} should be outside the original box" );

			var transformedPoint = transform.PointToWorld( point );

			// point should remain outside
			Assert.IsFalse( transformedBBox.Contains( transformedPoint ),
				$"Point {point} transformed to {transformedPoint} should remain outside the transformed box {transformedBBox}" );
		}
	}

	[TestMethod]
	public void CombinedTransform()
	{
		var bbox = new BBox( new Vector3( -5, -10, -15 ), new Vector3( 5, 10, 15 ) );

		// Apply translation, rotation, and scale
		var transform = new Transform(
			new Vector3( 100, 200, 300 ),
			Rotation.From( 45, 30, 60 ),
			new Vector3( 2, 3, 4 )
		);

		var transformedBBox = bbox.Transform( transform );

		// Test that all original corners, when transformed individually, are contained in the transformed bbox
		foreach ( var corner in bbox.Corners )
		{
			var transformedCorner = transform.PointToWorld( corner );
			Assert.IsTrue( transformedBBox.Contains( transformedCorner ),
				$"Corner {corner} transformed to {transformedCorner} is not contained in the bbox {transformedBBox}" );
		}

		var outsidePoints = new[]
		{
			new Vector3( -50, 0, 0 ),    // Far beyond min X
			new Vector3( 50, 0, 0 ),     // Far beyond max X
			new Vector3( 0, -50, 0 ),    // Far beyond min Y
			new Vector3( 0, 50, 0 ),     // Far beyond max Y
			new Vector3( 0, 0, -50 ),    // Far beyond min Z
			new Vector3( 0, 0, 50 )      // Far beyond max Z
		};

		foreach ( var point in outsidePoints )
		{
			// Verify point is outside original box
			Assert.IsFalse( bbox.Contains( point ),
				$"Test point {point} should be outside the original box" );

			var transformedPoint = transform.PointToWorld( point );

			// Test the transformed point against the transformed box
			Assert.IsFalse( transformedBBox.Contains( transformedPoint ),
				$"Point {point} transformed to {transformedPoint} should remain outside the transformed box {transformedBBox}" );
		}
	}

	[TestMethod]
	public void NonUniformScaleAsymmetricBox()
	{
		var bbox = new BBox( new Vector3( -5, -10, -20 ), new Vector3( 15, 10, 5 ) );

		Assert.IsTrue( bbox.Center.AlmostEqual( new Vector3( 5f, 0f, -7.5f ) ) );
		Assert.IsTrue( bbox.Extents.AlmostEqual( new Vector3( 10f, 10f, 12.5f ) ) );

		// Apply non-uniform scale
		var transform = new Transform();
		transform.Scale = new Vector3( 2, 3, 0.5f );
		var transformedBBox = bbox.Transform( transform );

		// Test that all original corners, when transformed individually, are contained in the transformed bbox
		foreach ( var corner in bbox.Corners )
		{
			var transformedCorner = transform.PointToWorld( corner );
			Assert.IsTrue( transformedBBox.Contains( transformedCorner ),
				$"Corner {corner} transformed to {transformedCorner} is not contained in the bbox {transformedBBox}" );
		}

		var outsidePoints = new[]
		{
			new Vector3( -6, 0, 0 ),     // Beyond min X
			new Vector3( 16, 0, 0 ),     // Beyond max X
			new Vector3( 0, -11, 0 ),    // Beyond min Y
			new Vector3( 0, 11, 0 ),     // Beyond max Y
			new Vector3( 0, 0, -21 ),    // Beyond min Z
			new Vector3( 0, 0, 6 )       // Beyond max Z
		};

		foreach ( var point in outsidePoints )
		{
			// Verify point is outside original box
			Assert.IsFalse( bbox.Contains( point ),
				$"Test point {point} should be outside the original box" );

			var transformedPoint = transform.PointToWorld( point );

			// Test the transformed point against the transformed box
			Assert.IsFalse( transformedBBox.Contains( transformedPoint ),
				$"Point {point} transformed to {transformedPoint} should remain outside the transformed box {transformedBBox}" );
		}
	}

	[TestMethod]
	public void ComplexTransformTest()
	{
		// Create test cases with various transformations and bbox shapes
		var testCases = new[]
		{
            // BBox, Transform
            (new BBox(new Vector3(-1, -2, -3), new Vector3(4, 5, 6)),
			 new Transform(new Vector3(10, 20, 30), Rotation.From(45, 30, 60), new Vector3(2, 3, 4))),

			(new BBox(new Vector3(0, 0, 0), new Vector3(10, 10, 10)),
			 new Transform(new Vector3(5, 5, 5), Rotation.From(0, 90, 0), new Vector3(0.5f, 0.5f, 0.5f))),

			(new BBox(new Vector3(-100, -50, -25), new Vector3(100, 200, 75)),
			 new Transform(new Vector3(-50, -50, -50), Rotation.From(180, 0, 0), new Vector3(0.1f, 10f, 1f))),
		};

		foreach ( var (box, transform) in testCases )
		{
			var transformedBox = box.Transform( transform );

			// Verify all corners are properly contained
			foreach ( var corner in box.Corners )
			{
				var transformedCorner = transform.PointToWorld( corner );
				Assert.IsTrue( transformedBox.Contains( transformedCorner ),
					$"Corner {corner} transformed to {transformedCorner} is not contained in the bbox with mins {transformedBox.Mins} and maxs {transformedBox.Maxs}" );
			}
		}
	}
}
