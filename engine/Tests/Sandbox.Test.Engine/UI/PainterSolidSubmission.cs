using Sandbox.UI;

namespace UITests;

/// <summary>
/// Compares direct shape submission with the general CSS descriptor path.
/// </summary>
[TestClass]
public class PainterSolidSubmissionTest : PainterTestBase
{
	/// <summary>
	/// Solid draws and gradient fallbacks preserve opacity, clipping, transform and shader defaults.
	/// </summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( true, true )]
	public void ShapeInstancesMatchGeneralSubmission( bool polygon, bool gradient )
	{
		var painter = Paint;
		var color = new Color( 0.2f, 0.4f, 0.8f, 0.75f );
		var fill = gradient ? Fill.LinearGradient( color, Color.Red ) : Fill.Solid( color );
		PaintContext.InheritedOpacity = 0.6f;
		PaintContext.BaseTransform = Matrix.CreateTranslation( new Vector3( 7, 11, 0 ) );
		painter.Opacity = 0.4f;
		painter.Transform = Matrix.CreateTranslation( new Vector3( 13, 17, 0 ) );
		painter.BlendMode = BlendMode.Multiply;
		PaintContext.State.FillInsets = new Vector4( 5, 6, 7, 8 );
		PaintContext.Batcher.Destination.SetScissor( Painter.Scissoring.Single( new Rect( 0, 0, 500, 500 ), BorderRadii.Zero, Matrix.Identity ) );
		painter.Clip( new Rect( 0, 0, 200, 200 ) );

		Rect bounds;
		if ( polygon )
		{
			painter.Fill = fill;
			painter.Polygon( [new Vector2( 10, 20 ), new Vector2( 90, 20 ), new Vector2( 90, 80 )] );
			bounds = new Rect( 10, 20, 80, 60 );
		}
		else
		{
			painter.Stroke = Stroke.Solid( fill, 4 );
			painter.Line( new Vector2( 10, 20 ), new Vector2( 90, 20 ) );
			bounds = new Rect( 8, 18, 84, 4 );
		}

		var batcher = PaintContext.Batcher;
		Assert.AreEqual( 1, batcher.Instances.Count );
		var actual = batcher.Instances[0];
		fill.CreateDescriptor( bounds, PaintContext, out var descriptor, clipFill: polygon );
		descriptor.ShapeIndex = actual.ShapeIndex;
		var transform = PaintContext.State.Transform * PaintContext.BaseTransform;
		batcher.Add( descriptor, PaintContext.State.Opacity, PaintContext.State.OverrideBlendMode, transform, PaintContext.State.ClipIndex );
		Assert.AreEqual( batcher.Instances[1], actual, "Direct and general submission must resolve identical GPU data." );
		Assert.AreEqual( 0, actual.Mode );
		Assert.AreEqual( -1, actual.InverseScissorIndex );
		Assert.IsTrue( actual.ShapeIndex >= 0 );
		Assert.IsTrue( actual.ScissorIndex >= 0 );
		Assert.AreEqual( polygon ? (int)BackgroundClip.ContentBox : (int)BackgroundClip.BorderBox, actual.BackgroundClip );
		Assert.AreEqual( PaintContext.State.FillInsets, actual.BackgroundClipRect );
		Assert.AreEqual( transform, batcher.Transforms[actual.TransformIndex].Mat );
		if ( gradient )
		{
			Assert.IsTrue( actual.TextureIndex < 0, "Gradients must take the descriptor path and resolve a gradient table entry." );
			Assert.AreEqual( 1, batcher.Gradients.Count );
		}
		else
		{
			Assert.AreEqual( color.WithAlphaMultiplied( 0.6f ).WithAlphaMultiplied( 0.4f ), actual.Color );
			Assert.AreEqual( 0, actual.TextureIndex );
		}

		int draws = batcher.DrawCalls;
		painter.BlendMode = BlendMode.Normal;
		painter.Stroke = Stroke.Solid( Color.White, 4 );
		painter.Line( new Vector2( 10, 20 ), new Vector2( 90, 20 ) );
		Assert.AreEqual( draws + 1, batcher.DrawCalls, "Changing blend mode must flush the previous batch." );
	}
}
