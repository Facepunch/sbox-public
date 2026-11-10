using System;
using System.Linq;

namespace UITests;

[TestClass]
public class PainterStrokePatternTest
{
	[TestMethod]
	public void ConstructedStrokesPreserveDashDefaults()
	{
		var stroke = new Stroke { Style = BorderStyle.Dashed };
		Assert.AreEqual( 8f, stroke.DashLength );
		Assert.AreEqual( 4f, stroke.Gap );
		Assert.AreEqual( 0f, stroke.Offset );
		Assert.AreEqual( BorderStyle.Solid, default( Stroke ).Style );
		Assert.AreEqual( 8f, Stroke.Solid( Color.White ).DashLength );
		Assert.AreEqual( 4f, Stroke.Solid( Color.White ).Gap );
	}

	[TestMethod]
	public void StrokeCopiesHaveIndependentPatternSettings()
	{
		var red = Stroke.Dashed( Color.Red, 3, 12, 6, 2 );
		var blue = red with { Fill = Color.Blue, Width = 8, Offset = 9 };
		Assert.AreEqual( 2f, red.Offset );
		Assert.AreEqual( 9f, blue.Offset );
		Assert.AreEqual( 12f, blue.DashLength );
		Assert.AreEqual( 6f, blue.Gap );
		Assert.AreEqual( 3f, red.Width );
		Assert.AreEqual( 8f, blue.Width );
	}

	[TestMethod]
	public void StrokeFactoriesSetDirectStyleAndSpacing()
	{
		Assert.AreEqual( Stroke.Solid( Color.Red, 2 ) with { Style = BorderStyle.Dashed, DashLength = 12, Gap = 6, Offset = -3 }, Stroke.Dashed( Color.Red, 2, 12, 6, -3 ) );
		Assert.AreEqual( Stroke.Solid( Color.Red, 2 ) with { Style = BorderStyle.Dotted, Gap = 6, Offset = -3 }, Stroke.Dotted( Color.Red, 2, 6, -3 ) );
	}

	[TestMethod]
	public void ExplicitRectangleBordersUseOnlyTheStrokeStyle()
	{
		var context = new Painter.Context( new Sandbox.Rendering.CommandList() );
		context.Begin( new Rect( 0, 0, 100, 100 ) );
		try
		{
			var painter = context.Painter;
			var stroke = Stroke.Solid( Fill.None, 0 ).WithAlignment( Stroke.StrokeAlignment.Outside )
				with
			{ Style = BorderStyle.Double };
			painter.Stroke = stroke;
			painter.Rect( new Rect( 0, 0, 50, 50 ), new Vector4( 2, 4, 6, 8 ), Color.Red, Color.Green, Color.Blue, Color.White );
			var box = context.Batcher.Instances.Single();
			Assert.AreEqual( new Vector4( 2, 4, 6, 8 ), box.BorderSize );
			Assert.AreEqual( (int)BorderStyle.Double, (int)box.BorderStyle );
			Assert.AreEqual( stroke, painter.Stroke );
			Assert.AreEqual( 0, context.Batcher.Paths.Count );
		}
		finally
		{
			context.CommandList.Reset();
			context.Batcher.Dispose();
		}
	}

	[TestMethod]
	public void ExplicitRectangleBordersHonorDisabledStyles()
	{
		var context = new Painter.Context( new Sandbox.Rendering.CommandList() );
		context.Begin( new Rect( 0, 0, 100, 100 ) );
		try
		{
			var painter = context.Painter;
			foreach ( var style in new[] { BorderStyle.None, BorderStyle.Hidden } )
			{
				painter.Stroke = new Stroke { Style = style };
				painter.Rect( new Rect( 0, 0, 50, 50 ), new Vector4( 2, 4, 6, 8 ), Color.Red, Color.Green, Color.Blue, Color.White );
			}
			Assert.AreEqual( 0, context.Batcher.Instances.Count );
			painter.Stroke = Stroke.None;
			painter.Rect( new Rect( 0, 0, 50, 50 ), new Vector4( 2, 4, 6, 8 ), Color.Red, Color.Green, Color.Blue, Color.White );
			Assert.AreEqual( 1, context.Batcher.Instances.Count, "Default pattern is solid even when the uniform stroke has no width." );
		}
		finally
		{
			context.CommandList.Reset();
			context.Batcher.Dispose();
		}
	}

	[TestMethod]
	public void UnsupportedPathStylesDrawTheSameGeometryAsSolid()
	{
		static (UICssBoxBatched.PathPrimitive[] Paths, int Instances) Draw( BorderStyle style )
		{
			var context = new Painter.Context( new Sandbox.Rendering.CommandList() );
			context.Begin( new Rect( 0, 0, 100, 100 ) );
			try
			{
				var painter = context.Painter;
				painter.Fill = Color.Red;
				painter.Stroke = Stroke.Solid( Color.White, 4 ) with { Style = style };
				painter.Circle( new Vector2( 50 ), 20 );
				painter.Line( new Vector2( 10 ), new Vector2( 90 ) );
				painter.Polygon( [new Vector2( 10 ), new Vector2( 90, 10 ), new Vector2( 50, 90 )] );
				painter.Rect( new Rect( 10, 10, 80, 80 ) );
				painter.Stroke = painter.Stroke with
				{
					Alignment = Stroke.StrokeAlignment.Inside,
					Fill = Fill.LinearGradient().WithStop( 0, Color.Red ).WithStop( 1, Color.Blue )
				};
				painter.Rect( new Rect( 10, 10, 80, 80 ) );
				return (context.Batcher.Paths.ToArray(), context.Batcher.Instances.Count);
			}
			finally
			{
				context.CommandList.Reset();
				context.Batcher.Dispose();
			}
		}

		var solid = Draw( BorderStyle.Solid );
		Assert.IsTrue( solid.Paths.Length > 0 );
		Assert.IsTrue( solid.Instances > 0 );
		foreach ( var style in new[] { BorderStyle.Double, BorderStyle.Groove, BorderStyle.Ridge, BorderStyle.Inset, BorderStyle.Outset } )
		{
			var fallback = Draw( style );
			Assert.AreEqual( solid.Instances, fallback.Instances, style.ToString() );
			CollectionAssert.AreEqual( solid.Paths, fallback.Paths, style.ToString() );
		}
	}

	[TestMethod]
	public void UniformInsideRectanglesSupportDecorativeStyles()
	{
		foreach ( var style in new[] { BorderStyle.Double, BorderStyle.Groove, BorderStyle.Ridge, BorderStyle.Inset, BorderStyle.Outset } )
		{
			var stroke = Stroke.Solid( Color.White, 6 ).WithAlignment( Stroke.StrokeAlignment.Inside )
				with
			{ Style = style };
			Assert.IsTrue( Painter.TryGetBoxStroke( stroke, out var box ) );
			Assert.AreEqual( style, box.Style );
			Assert.AreEqual( new Vector4( 6 ), box.Size );
		}
	}
}
