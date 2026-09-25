using Sandbox.Rendering;
using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	sealed class StrokePanel : Panel
	{
		internal int Draws;
		public StrokePanel()
		{
			Style.Width = Length.Percent( 100 );
			Style.Height = Length.Percent( 100 );
		}
		public override void OnDraw( Painter painter )
		{
			Draws++;
			painter.Stroke = new Stroke( Color.White, 1 );
			painter.Line( painter.Bounds.Center - new Vector2( 200, 0 ), painter.Bounds.Center + new Vector2( 200, 0 ) );
		}
	}

	/// <summary>Drawing uses local coordinates under changing ancestor transforms.</summary>
	[TestMethod]
	public void AncestorScaleKeepsStrokeInLocalCoordinates()
	{
		var root = new RootPanel { RenderedManually = true, PanelBounds = new Rect( 0, 0, 512, 512 ) };
		var child = root.AddChild<StrokePanel>();
		try
		{
			root.Layout();
			var first = PanelDrawSnapshot.Build( root );
			var bounds = first.Instances.Single().Rect;
			Assert.AreEqual( 1, child.Draws );
			root.Style.Set( "transform", "scale(0.01)" );
			root.Layout();
			var scaled = PanelDrawSnapshot.Build( root );
			Assert.AreEqual( 2, child.Draws );
			Assert.AreEqual( bounds, scaled.Instances.Single().Rect );
			CollectionAssert.AreEqual( first.Paths, scaled.Paths );
			Assert.AreNotEqual( first.Transforms[0], scaled.Transforms[0] );
			root.Style.Set( "transform", "none" );
			root.Layout();
			var restored = PanelDrawSnapshot.Build( root );
			Assert.AreEqual( 3, child.Draws );
			CollectionAssert.AreEqual( first.Instances, restored.Instances );
			CollectionAssert.AreEqual( first.Transforms, restored.Transforms );
		}
		finally
		{
			root.Delete( true );
		}
	}

	/// <summary>One path preserves ordering and paint without mutable instance ranges.</summary>
	[TestMethod]
	public void StrokesPreserveDrawOrder()
	{
		WithBuffer( layer =>
		{
			PaintStroke = Stroke.None;
			PaintFill = Color.Green;
			Paint.Rect( new Rect( 0, 0, 10, 10 ) );
			PaintContext.InheritedOpacity = 0.5f;
			PaintContext.State.OverrideBlendMode = BlendMode.Multiply;
			PaintStroke = new Stroke( Fill.LinearGradient( Color.Red, Color.Blue ), 1 );
			Paint.Line( Vector2.Zero, new Vector2( 1000, 0 ) );
			PaintStroke = new Stroke( Color.White, 1 );
			Paint.Arc( new Vector2( 500, 300 ), 200, 0, 360 );
			PaintStroke = Stroke.None;
			PaintFill = Color.Blue;
			Paint.Rect( new Rect( 0, 0, 10, 10 ) );
			Assert.AreEqual( 4, layer.Instances.Count );
			var line = layer.Instances[1];
			Assert.AreEqual( BlendMode.Multiply, layer.BlendMode );
			Assert.AreEqual( Color.White.WithAlpha( 0.5f ), line.GPU.BackgroundTint );
			Assert.AreEqual( new Vector4( -0.5f, -0.5f, 1001, 1 ), line.GPU.Rect );
			Assert.AreEqual( new Vector4( 0, 0, 1001, 1 ), line.GPU.BackgroundRect );
			AssertSimpleLine( line, Vector2.Zero, new Vector2( 1000, 0 ), PaintStroke.Cap );
		} );
	}

	/// <summary>Dense patterns retain their geometry; unrepresentable buffer sizes fail before generating it.</summary>
	[TestMethod]
	public void PatternGenerationRespectsBufferCapacity()
	{
		WithBuffer( layer =>
		{
			foreach ( var pattern in new[] { BorderStyle.Dotted, BorderStyle.Dashed } )
			{
				var stroke = new Stroke( Color.White, 1e-20f ) { Style = pattern, DashLength = 1e-20f, Gap = 1e-20f };
				// The duplicate endpoint selects general path generation. Analytic two-point
				// lines do not allocate geometry per dash and have no such capacity limit.
				Action[] draws = [
					() => Paint.Line( [Vector2.Zero, Vector2.Zero, new Vector2( 1000, 0 )] ),
					() => Paint.Polygon( [Vector2.Zero, new Vector2( 1000, 0 ), new Vector2( 1000, 1000 )] ),
					() => Paint.Arc( Vector2.Zero, 200, 15, -270 ),
					() => Paint.Arc( Vector2.Zero, 200, 0, 360 )];
				PaintStroke = stroke;
				foreach ( var draw in draws )
				{
					var error = Assert.ThrowsException<ArgumentOutOfRangeException>( draw );
					Assert.AreEqual( "stroke", error.ParamName );
					Assert.AreEqual( 0, layer.Instances.Count );
				}
			}
			foreach ( var pattern in new[] { BorderStyle.Dotted, BorderStyle.Dashed } )
			{
				layer.Clear();
				var accepted = new Stroke( Color.White, 0.125f ) { Style = pattern, DashLength = 0.125f, Gap = 0.125f };
				PaintStroke = accepted;
				// Keep this check on generated geometry rather than the analytic line representation.
				Paint.Line( [Vector2.Zero, Vector2.Zero, new Vector2( 50000 * 0.25f, 0 )] );
				Assert.AreEqual( pattern == BorderStyle.Dotted ? 50001 : 50000, Primitives( layer ).Length, "An open dotted path includes both endpoints." );
			}
		} );
	}
}
