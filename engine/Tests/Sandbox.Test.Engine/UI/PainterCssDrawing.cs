using Sandbox.UI;

namespace UITests;

[TestClass]
public class PainterCssDrawingTest
{
	readonly Painter.Context _context = new( new Sandbox.Rendering.CommandList() );
	Painter Paint => _context.Painter;

	[TestInitialize]
	public void BeginRecording() => _context.Begin( new Rect( 0, 0, 1000, 1000 ) );

	[TestCleanup]
	public void DiscardRecording()
	{
		_context.CommandList.Reset();
		_context.Batcher.Dispose();
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( true, true )]
	public void CssBoxMatchesDrawingState( bool gradient, bool invalidate )
	{
		var painter = Paint;
		var rect = new Rect( 20, 30, 120, 80 );
		var corners = new Painter.CornerRadii( new Vector2( 14, 8 ) );
		var fill = gradient ? Fill.LinearGradient( Color.Red, Color.Blue ) : (Fill)Color.Red;
		var stroke = Stroke.Solid( Color.Blue, 3 ).WithAlignment( Stroke.StrokeAlignment.Inside );
		var insets = new Vector4( 5, 6, 7, 8 );
		_context.InheritedOpacity = 0.5f;
		_context.ResetDrawingState( BlendMode.Multiply );
		painter.Fill = fill;
		painter.Stroke = Stroke.Solid( Color.Blue, 3 ).WithAlignment( Stroke.StrokeAlignment.Inside );
		painter.ClipFill( insets );
		painter.Rect( rect, corners );
		var expected = _context.Batcher.Instances[0];

		painter.Fill = Color.Green;
		painter.Opacity = 0;
		painter.Transform = Matrix.CreateScale( new Vector3( 0, 0, 1 ) );
		painter.BlendMode = BlendMode.Lighten;
		painter.Clip( new Rect( 0, 0, 1, 1 ) );
		_context.BaseTransform = Matrix.CreateTranslation( new Vector3( 100, 200, 0 ) );
		if ( invalidate ) _context.ResetDrawingState( BlendMode.Multiply );

		var descriptor = fill.CreateDescriptor( rect, 1, BlendMode.Normal, insets );
		descriptor.Radii = corners.Resolve( rect );
		descriptor.Stroke = Painter.ResolveBoxStroke( stroke );
		painter.Rect( in descriptor );

		Assert.AreEqual( 2, _context.Batcher.Count );
		Assert.AreEqual( expected, _context.Batcher.Instances[1] );
		Assert.AreEqual( !invalidate, _context.HasState );
		Assert.AreEqual( 0f, _context.State.Opacity );
		Assert.AreEqual( (Fill)Color.Green, _context.State.Fill );
	}

	[TestMethod]
	public void CachedBoxUsesCurrentOpacityWithoutChangingTheDescriptor()
	{
		var rect = new Rect( 20, 30, 120, 80 );
		var fill = Fill.LinearGradient( Color.Red, Color.Blue );
		var stroke = Stroke.Solid( Color.Green.WithAlpha( 0.8f ), 3 ).WithAlignment( Stroke.StrokeAlignment.Inside );
		var descriptor = fill.CreateDescriptor( rect, 1, BlendMode.Normal, default );
		descriptor.Stroke = Painter.ResolveBoxStroke( stroke );
		var original = descriptor;

		foreach ( var opacity in new[] { 0.25f, 0.75f, 1f } )
		{
			_context.InheritedOpacity = opacity;
			Paint.Rect( in descriptor );

			var expected = fill.CreateDescriptor( rect, opacity, BlendMode.Normal, default );
			expected.Stroke = Painter.ResolveBoxStroke( stroke ).WithAlphaMultiplied( opacity );
			var gpu = _context.Batcher.Resolve( expected, Matrix.Identity, -1 );
			Assert.AreEqual( gpu, _context.Batcher.Instances[^1] );
			Assert.AreEqual( original, descriptor );
		}
	}

	[TestMethod]
	public void RectShadowDefaultsMatchExplicitBlackSquareShadow()
	{
		var painter = Paint;
		var rect = new Rect( 20, 30, 120, 80 );
		painter.RectShadow( rect );
		painter.RectShadow( rect, 0, color: Color.Black );
		Assert.AreEqual( _context.Batcher.Instances[0], _context.Batcher.Instances[1] );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CssShadowMatchesDrawingState( bool inset )
	{
		var painter = Paint;
		var rect = new Rect( 20, 30, 120, 80 );
		var corners = new Painter.CornerRadii( new Vector2( 14, 8 ) );
		_context.InheritedOpacity = 0.5f;
		_context.ResetDrawingState( BlendMode.Lighten );
		painter.RectShadow( rect, corners, color: Color.Red, blur: 6, spread: 2, offset: new Vector2( 3, 4 ), inset: inset );
		var expected = _context.Batcher.Instances[0];

		painter.Opacity = 0;
		painter.Transform = Matrix.CreateScale( new Vector3( 0, 0, 1 ) );
		_context.ResetDrawingState( BlendMode.Lighten );
		painter.RectShadow( rect, corners.Resolve( rect ), color: Color.Red, blur: 6, spread: 2, offset: new Vector2( 3, 4 ), inset: inset );

		Assert.AreEqual( 2, _context.Batcher.Count );
		Assert.AreEqual( expected, _context.Batcher.Instances[1] );
		Assert.IsFalse( _context.HasState );
	}

	[TestMethod]
	public void CssOutlineDoesNotInitializeDrawingState()
	{
		var painter = Paint;
		_context.InheritedOpacity = 0.5f;
		painter.Opacity = 0;
		painter.Transform = Matrix.CreateScale( new Vector3( 0, 0, 1 ) );
		_context.ResetDrawingState( BlendMode.Normal );

		painter.Outline( new Rect( 20, 30, 120, 80 ), Color.Red, 3, BorderRadii.Zero, 2 );

		Assert.AreEqual( 1, _context.Batcher.Count );
		Assert.IsFalse( _context.HasState );
		Assert.AreEqual( 0f, _context.State.Opacity );
	}
}
