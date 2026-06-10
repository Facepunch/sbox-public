using Sandbox.UI;

namespace UITest.PropertyCoverage;

/// <summary>
/// Exhaustive coverage of the CSS transform family:
///   transform           -> Transform (PanelTransform?)
///   transform-origin     -> TransformOriginX / TransformOriginY
///   transform-origin-x   -> TransformOriginX
///   transform-origin-y   -> TransformOriginY
///   perspective-origin   -> PerspectiveOriginX / PerspectiveOriginY
///   perspective-origin-x -> PerspectiveOriginX
///   perspective-origin-y -> PerspectiveOriginY
///
/// The transform matrix is asserted by building it with the same dimensions the
/// engine uses in the existing parser tests: BuildTransform( 1000, 1000, Vector2.Zero ).
/// A single transform-function produces exactly one Entry, so the built matrix equals
/// the corresponding Matrix.Create*(...) for that single function.
/// </summary>
[TestClass]
public class TransformProperties
{
	// Helper: build the matrix for a transform value using the canonical 1000x1000 / zero-origin args.
	private static Matrix Build( string transformValue )
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "transform", transformValue ), $"Set failed for '{transformValue}'" );
		Assert.IsTrue( s.Transform.HasValue, $"Transform null for '{transformValue}'" );
		return s.Transform.Value.BuildTransform( 1000, 1000, Vector2.Zero );
	}

	// ---------------------------------------------------------------------
	// transform: none / empty
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TransformNone()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "transform", "scale(2)" ) );
		Assert.IsTrue( s.Transform.HasValue );

		// 'none' resets it to the identity transform (non-null, so it overrides a base rule)
		Assert.IsTrue( s.Set( "transform", "none" ) );
		Assert.IsTrue( s.Transform.HasValue );
	}

	[TestMethod]
	public void TransformEmptyString()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "transform", "scale(2)" ) );
		Assert.IsTrue( s.Transform.HasValue );

		// empty string is treated the same as none -> reset to the identity transform
		Assert.IsTrue( s.Set( "transform", "" ) );
		Assert.IsTrue( s.Transform.HasValue );
	}

	// ---------------------------------------------------------------------
	// translate
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TransformTranslate()
	{
		// width=1000, height=1000 so pixel lengths pass through unchanged
		var m = Build( "translate(10px 20px)" );
		Assert.AreEqual( Matrix.CreateTranslation( new Vector3( 10, 20, 0 ) ), m );
	}

	[TestMethod]
	public void TransformTranslateCommaSeparated()
	{
		// "10px, 20px" - comma followed by a space; the first word includes the comma
		// but Length.Parse tolerates the trailing comma, and the second value is read.
		var m = Build( "translate(10px, 20px)" );
		Assert.AreEqual( Matrix.CreateTranslation( new Vector3( 10, 20, 0 ) ), m );
	}

	[TestMethod]
	public void TransformTranslateSingleArg()
	{
		// translate(10px) -> Y defaults to 0
		var m = Build( "translate(10px)" );
		Assert.AreEqual( Matrix.CreateTranslation( new Vector3( 10, 0, 0 ) ), m );
	}

	[TestMethod]
	public void TransformTranslatePercent()
	{
		// 50% of width(1000) = 500, 25% of height(1000) = 250
		var m = Build( "translate(50% 25%)" );
		Assert.AreEqual( Matrix.CreateTranslation( new Vector3( 500, 250, 0 ) ), m );
	}

	[TestMethod]
	public void TransformTranslateX()
	{
		var m = Build( "translateX(10px)" );
		Assert.AreEqual( Matrix.CreateTranslation( new Vector3( 10, 0, 0 ) ), m );
	}

	[TestMethod]
	public void TransformTranslateY()
	{
		var m = Build( "translateY(20px)" );
		Assert.AreEqual( Matrix.CreateTranslation( new Vector3( 0, 20, 0 ) ), m );
	}

	[TestMethod]
	public void TransformTranslateZ()
	{
		// Z uses GetPixels( 0 ) so a pixel value passes through unchanged
		var m = Build( "translateZ(30px)" );
		Assert.AreEqual( Matrix.CreateTranslation( new Vector3( 0, 0, 30 ) ), m );
	}

	[TestMethod]
	public void TransformTranslate3d()
	{
		var m = Build( "translate3d(10px 20px 30px)" );
		Assert.AreEqual( Matrix.CreateTranslation( new Vector3( 10, 20, 30 ) ), m );
	}

	// ---------------------------------------------------------------------
	// scale
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TransformScaleSingle()
	{
		// scale(2) -> x=y=2, z stays 1
		var m = Build( "scale(2)" );
		Assert.AreEqual( Matrix.CreateScale( new Vector3( 2, 2, 1 ) ), m );
	}

	[TestMethod]
	public void TransformScaleTwoArgs()
	{
		// space-separated two args are parsed correctly
		var m = Build( "scale(2 0.5)" );
		Assert.AreEqual( Matrix.CreateScale( new Vector3( 2, 0.5f, 1 ) ), m );
	}

	[TestMethod]
	public void TransformScaleX()
	{
		var m = Build( "scaleX(2)" );
		Assert.AreEqual( Matrix.CreateScale( new Vector3( 2, 1, 1 ) ), m );
	}

	[TestMethod]
	public void TransformScaleY()
	{
		var m = Build( "scaleY(3)" );
		Assert.AreEqual( Matrix.CreateScale( new Vector3( 1, 3, 1 ) ), m );
	}

	[TestMethod]
	public void TransformScaleZ()
	{
		var m = Build( "scaleZ(4)" );
		Assert.AreEqual( Matrix.CreateScale( new Vector3( 1, 1, 4 ) ), m );
	}

	[TestMethod]
	public void TransformScale3d()
	{
		var m = Build( "scale3d(2 3 4)" );
		Assert.AreEqual( Matrix.CreateScale( new Vector3( 2, 3, 4 ) ), m );
	}

	[TestMethod]
	public void TransformScaleCommaArgs()
	{
		// scale() takes comma-separated args: scale(2, 0.5) => x=2, y=0.5.
		var m = Build( "scale(2, 0.5)" );
		Assert.AreEqual( Matrix.CreateScale( new Vector3( 2, 0.5f, 1 ) ), m );
	}

	// ---------------------------------------------------------------------
	// rotate
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TransformRotateDeg()
	{
		// rotate() with no axis is treated as rotation about Z
		var m = Build( "rotate(10deg)" );
		Assert.AreEqual( Matrix.CreateRotation( new Vector3( 0, 0, 10 ) ), m );
	}

	[TestMethod]
	public void TransformRotateUnitless()
	{
		// no unit -> treated as degrees
		var m = Build( "rotate(10)" );
		Assert.AreEqual( Matrix.CreateRotation( new Vector3( 0, 0, 10 ) ), m );
	}

	[TestMethod]
	public void TransformRotateTurn()
	{
		// 0.5turn == 180deg
		var m = Build( "rotate(0.5turn)" );
		Assert.AreEqual( Matrix.CreateRotation( new Vector3( 0, 0, 180 ) ), m );
	}

	[TestMethod]
	public void TransformRotateNegativeTurn()
	{
		var m = Build( "rotate(-0.5turn)" );
		Assert.AreEqual( Matrix.CreateRotation( new Vector3( 0, 0, -180 ) ), m );
	}

	[TestMethod]
	public void TransformRotateZ()
	{
		var m = Build( "rotateZ(10deg)" );
		Assert.AreEqual( Matrix.CreateRotationZ( 10 ), m );
	}

	[TestMethod]
	public void TransformRotateX()
	{
		var m = Build( "rotateX(10deg)" );
		Assert.AreEqual( Matrix.CreateRotation( new Vector3( 10, 0, 0 ) ), m );
	}

	[TestMethod]
	public void TransformRotateY()
	{
		var m = Build( "rotateY(10deg)" );
		Assert.AreEqual( Matrix.CreateRotation( new Vector3( 0, 10, 0 ) ), m );
	}

	[TestMethod]
	public void TransformRotate3d()
	{
		// NOTE: this parser reads rotate3d as three angle components (x y z), not the
		// CSS axis+angle form. We assert the behaviour the engine actually implements.
		var m = Build( "rotate3d(45deg 0 0)" );
		Assert.AreEqual( Matrix.CreateRotation( new Vector3( 45, 0, 0 ) ), m );
	}

	// ---------------------------------------------------------------------
	// skew
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TransformSkewX()
	{
		// skewX(10deg) -> Data = (10, 0, 0)
		var ax = System.MathF.Tan( 10f.DegreeToRadian() );
		var expected = Matrix.CreateMatrix3D( new float[]
		{
			1.0f, 0.0f, 0.0f, 0.0f,
			ax,   1.0f, 0.0f, 0.0f,
			0.0f, 0.0f, 1.0f, 0.0f,
			0.0f, 0.0f, 0.0f, 1.0f
		} );

		var m = Build( "skewX(10deg)" );
		Assert.AreEqual( expected, m );
	}

	[TestMethod]
	public void TransformSkewY()
	{
		// skewY(10deg) -> Data = (0, 10, 0)
		var ay = System.MathF.Tan( 10f.DegreeToRadian() );
		var expected = Matrix.CreateMatrix3D( new float[]
		{
			1.0f, ay,   0.0f, 0.0f,
			0.0f, 1.0f, 0.0f, 0.0f,
			0.0f, 0.0f, 1.0f, 0.0f,
			0.0f, 0.0f, 0.0f, 1.0f
		} );

		var m = Build( "skewY(10deg)" );
		Assert.AreEqual( expected, m );
	}

	[TestMethod]
	public void TransformSkewTwoArgs()
	{
		// skew(10deg 20deg) -> Data = (10, 20, 0)
		var ax = System.MathF.Tan( 10f.DegreeToRadian() );
		var ay = System.MathF.Tan( 20f.DegreeToRadian() );
		var expected = Matrix.CreateMatrix3D( new float[]
		{
			1.0f, ay,   0.0f, 0.0f,
			ax,   1.0f, 0.0f, 0.0f,
			0.0f, 0.0f, 1.0f, 0.0f,
			0.0f, 0.0f, 0.0f, 1.0f
		} );

		var m = Build( "skew(10deg 20deg)" );
		Assert.AreEqual( expected, m );
	}

	// ---------------------------------------------------------------------
	// matrix / matrix3d
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TransformMatrix()
	{
		// matrix(a,b,c,d,e,f) maps into a 4x4: a b 0 0 / c d 0 0 / 0 0 1 0 / e f 0 1
		// matrix( 1, 0, 0, 1, 10, 20 ) is a pure translate by (10,20)
		var expected = Matrix.CreateMatrix3D( new float[]
		{
			1, 0, 0, 0,
			0, 1, 0, 0,
			0, 0, 1, 0,
			10, 20, 0, 1
		} );

		var m = Build( "matrix(1, 0, 0, 1, 10, 20)" );
		Assert.AreEqual( expected, m );
	}

	[TestMethod]
	public void TransformMatrix3d()
	{
		// identity matrix3d should build to identity
		var ident = new float[]
		{
			1, 0, 0, 0,
			0, 1, 0, 0,
			0, 0, 1, 0,
			0, 0, 0, 1
		};

		var m = Build( "matrix3d(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1)" );
		Assert.AreEqual( Matrix.CreateMatrix3D( ident ), m );
	}

	// ---------------------------------------------------------------------
	// perspective
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TransformPerspective()
	{
		// With perspectiveOrigin = Vector2.Zero the surrounding translations are identity,
		// so the built matrix is exactly the perspective matrix from ToMatrix.
		// d = 500px -> -1/max(500,1) in [2][3].
		float d = 500.0f;
		var expected = Matrix.CreateMatrix3D( new float[]
		{
			1.0f, 0.0f, 0.0f, 0.0f,
			0.0f, 1.0f, 0.0f, 0.0f,
			0.0f, 0.0f, 1.0f, -1.0f / System.MathF.Max( d, 1.0f ),
			0.0f, 0.0f, 0.0f, 1.0f
		} );

		var m = Build( "perspective(500px)" );
		Assert.AreEqual( expected, m );
	}

	// ---------------------------------------------------------------------
	// transform-origin (shorthand)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TransformOriginSingleValue()
	{
		// one value sets both X and Y
		var s = new Styles();
		Assert.IsTrue( s.Set( "transform-origin", "10px" ) );

		Assert.IsTrue( s.TransformOriginX.HasValue );
		Assert.IsTrue( s.TransformOriginY.HasValue );
		Assert.AreEqual( 10, s.TransformOriginX.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TransformOriginX.Value.Unit );
		Assert.AreEqual( 10, s.TransformOriginY.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TransformOriginY.Value.Unit );
	}

	[TestMethod]
	public void TransformOriginTwoValues()
	{
		// two values set X then Y
		var s = new Styles();
		Assert.IsTrue( s.Set( "transform-origin", "10px 20px" ) );

		Assert.AreEqual( 10, s.TransformOriginX.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TransformOriginX.Value.Unit );
		Assert.AreEqual( 20, s.TransformOriginY.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TransformOriginY.Value.Unit );
	}

	[TestMethod]
	public void TransformOriginPercent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "transform-origin", "50% 75%" ) );

		Assert.AreEqual( 50, s.TransformOriginX.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.TransformOriginX.Value.Unit );
		Assert.AreEqual( 75, s.TransformOriginY.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.TransformOriginY.Value.Unit );
	}

	[TestMethod]
	public void TransformOriginInvalid()
	{
		var s = new Styles();
		// no parseable length at all -> false
		Assert.IsFalse( s.Set( "transform-origin", "bullshit" ) );
		Assert.IsFalse( s.TransformOriginX.HasValue );
		Assert.IsFalse( s.TransformOriginY.HasValue );
	}

	[TestMethod]
	public void TransformOriginX()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "transform-origin-x", "33px" ) );
		Assert.IsTrue( s.TransformOriginX.HasValue );
		Assert.AreEqual( 33, s.TransformOriginX.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TransformOriginX.Value.Unit );
		// only X is set
		Assert.IsFalse( s.TransformOriginY.HasValue );
	}

	[TestMethod]
	public void TransformOriginY()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "transform-origin-y", "44%" ) );
		Assert.IsTrue( s.TransformOriginY.HasValue );
		Assert.AreEqual( 44, s.TransformOriginY.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.TransformOriginY.Value.Unit );
		// only Y is set
		Assert.IsFalse( s.TransformOriginX.HasValue );
	}

	[TestMethod]
	public void TransformOriginXInvalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "transform-origin-x", "bullshit" ) );
		Assert.IsFalse( s.TransformOriginX.HasValue );
	}

	/// <summary>
	/// transform-origin supports keyword positions (left/center/right/top/bottom) via Length.Parse
	/// </summary>
	[TestMethod]
	public void TransformOriginKeywords()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "transform-origin", "left top" ) );

		Assert.IsTrue( s.TransformOriginX.HasValue );
		Assert.IsTrue( s.TransformOriginY.HasValue );
		// "left" and "top" both map to the Start unit
		Assert.AreEqual( LengthUnit.Start, s.TransformOriginX.Value.Unit );
		Assert.AreEqual( LengthUnit.Start, s.TransformOriginY.Value.Unit );
	}

	[TestMethod]
	public void TransformOriginCenterKeyword()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "transform-origin", "center" ) );

		Assert.IsTrue( s.TransformOriginX.HasValue );
		Assert.IsTrue( s.TransformOriginY.HasValue );
		Assert.AreEqual( LengthUnit.Center, s.TransformOriginX.Value.Unit );
		Assert.AreEqual( LengthUnit.Center, s.TransformOriginY.Value.Unit );
	}

	// ---------------------------------------------------------------------
	// perspective-origin (shorthand)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void PerspectiveOriginSingleValue()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "perspective-origin", "12px" ) );

		Assert.IsTrue( s.PerspectiveOriginX.HasValue );
		Assert.IsTrue( s.PerspectiveOriginY.HasValue );
		Assert.AreEqual( 12, s.PerspectiveOriginX.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.PerspectiveOriginX.Value.Unit );
		Assert.AreEqual( 12, s.PerspectiveOriginY.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.PerspectiveOriginY.Value.Unit );
	}

	[TestMethod]
	public void PerspectiveOriginTwoValues()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "perspective-origin", "12px 34px" ) );

		Assert.AreEqual( 12, s.PerspectiveOriginX.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.PerspectiveOriginX.Value.Unit );
		Assert.AreEqual( 34, s.PerspectiveOriginY.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.PerspectiveOriginY.Value.Unit );
	}

	[TestMethod]
	public void PerspectiveOriginPercent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "perspective-origin", "25% 50%" ) );

		Assert.AreEqual( 25, s.PerspectiveOriginX.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.PerspectiveOriginX.Value.Unit );
		Assert.AreEqual( 50, s.PerspectiveOriginY.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.PerspectiveOriginY.Value.Unit );
	}

	[TestMethod]
	public void PerspectiveOriginInvalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "perspective-origin", "bullshit" ) );
		Assert.IsFalse( s.PerspectiveOriginX.HasValue );
		Assert.IsFalse( s.PerspectiveOriginY.HasValue );
	}

	[TestMethod]
	public void PerspectiveOriginX()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "perspective-origin-x", "7px" ) );
		Assert.IsTrue( s.PerspectiveOriginX.HasValue );
		Assert.AreEqual( 7, s.PerspectiveOriginX.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.PerspectiveOriginX.Value.Unit );
		Assert.IsFalse( s.PerspectiveOriginY.HasValue );
	}

	[TestMethod]
	public void PerspectiveOriginY()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "perspective-origin-y", "8%" ) );
		Assert.IsTrue( s.PerspectiveOriginY.HasValue );
		Assert.AreEqual( 8, s.PerspectiveOriginY.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.PerspectiveOriginY.Value.Unit );
		Assert.IsFalse( s.PerspectiveOriginX.HasValue );
	}

	[TestMethod]
	public void PerspectiveOriginYInvalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "perspective-origin-y", "bullshit" ) );
		Assert.IsFalse( s.PerspectiveOriginY.HasValue );
	}
}
