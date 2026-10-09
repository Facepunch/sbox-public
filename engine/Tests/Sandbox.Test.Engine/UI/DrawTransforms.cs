using Sandbox.UI;
using System;

namespace UITests;

[TestClass]
public class DrawTransformTest : PainterTestBase
{
	[TestMethod]
	public void HelpersUseTheCurrentOriginAndAxes()
	{
		using var scope = Paint.Scope();
		PaintTransform = Matrix.Identity;
		Paint.Translate( new Vector2( 100, 200 ) );
		Paint.Rotate( 90 );
		Paint.Scale( new Vector2( 2, 3 ) );
		var transform = PaintTransform;
		AssertPoint( new Vector2( 100, 200 ), transform.Transform( Vector2.Zero ) );
		AssertPoint( new Vector2( 100, 202 ), transform.Transform( new Vector2( 1, 0 ) ) );
		AssertPoint( new Vector2( 97, 200 ), transform.Transform( new Vector2( 0, 1 ) ) );
		using ( Paint.Scope() )
		{
			Paint.Translate( 5, 0 );
			AssertPoint( new Vector2( 100, 210 ), PaintTransform.Transform( Vector2.Zero ) );
		}
		Assert.AreEqual( transform, PaintTransform );
		PaintTransform = Matrix.Identity;
		Paint.Scale( 2 );
		AssertPoint( new Vector2( 2, 2 ), PaintTransform.Transform( Vector2.One ) );
	}

	static void AssertPoint( Vector2 expected, Vector2 actual )
	{
		Assert.AreEqual( expected.x, actual.x, 0.0001f );
		Assert.AreEqual( expected.y, actual.y, 0.0001f );
	}

	[TestMethod]
	public void InvalidTransformsLeaveTheStateIntact()
	{
		using var scope = Paint.Scope();
		PaintTransform = Matrix.CreateTranslation( new Vector3( 10, 20, 0 ) );
		var original = PaintTransform;
		Matrix[] invalid = [Matrix.CreateRotationX( 45 ), Matrix.Identity with { M14 = 1 },
			Matrix.Identity with { M41 = float.NaN }, Matrix.Identity with { M22 = float.PositiveInfinity }];
		foreach ( var matrix in invalid )
		{
			Assert.ThrowsException<ArgumentException>( () => PaintTransform = matrix );
			Assert.AreEqual( original, PaintTransform );
		}
		Assert.ThrowsException<ArgumentException>( () => Paint.Rotate( float.NaN ) );
		Assert.ThrowsException<ArgumentException>( () => Paint.Translate( float.PositiveInfinity, 0 ) );
		Assert.AreEqual( original, PaintTransform );
	}

	[TestMethod]
	public void TransformScopesRestoreState()
	{
		using var scope = Paint.Scope();
		PaintTransform = Matrix.Identity;
		using ( Paint.Scope() )
		{
			Paint.Translate( 10, 20 );
			Paint.Rotate( 90 );
			Paint.Scale( 2, 3 );
			AssertPoint( new Vector2( 7, 22 ), PaintTransform.Transform( Vector2.One ) );
		}
		Assert.AreEqual( Matrix.Identity, PaintTransform );
	}
}
