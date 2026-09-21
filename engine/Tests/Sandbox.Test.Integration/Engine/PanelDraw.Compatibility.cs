#pragma warning disable CS0618 // Intentionally exercises the legacy Panel.Draw API.

using Sandbox.Rendering;
using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	[TestMethod]
	[DataRow( 0.3f, false )]
	[DataRow( 0f, false )]
	[DataRow( 1f, true )]
	public void LegacyCommandsIgnoreMutablePainterState( float opacity, bool collapsed )
	{
		WithBuffer( layer =>
		{
			var rect = new Rect( 30, 40, 120, 60 );
			var color = Color.Red.WithAlpha( 0.8f );
			PaintContext.InheritedOpacity = 0.5f;
			PaintContext.BaseTransform = Matrix.CreateTranslation( new Vector3( 100, 200, 0 ) );
			System.Collections.Generic.List<Action> draws = [
				() => Panel.Draw.Rect( rect, color, 8 ),
				() => Panel.Draw.Circle( rect.Center, 20, color ),
				() => Panel.Draw.Texture( Texture.White, rect, color ),
				() => Panel.Draw.Shadow( rect, color, blur: 4 ),
				() => Panel.Draw.Outline( rect, color, width: 2 )];

			if ( !Application.IsHeadless )
				draws.Add( () => Panel.Draw.Text( "Legacy state", rect, 16, color ) );

			foreach ( var draw in draws )
			{
				layer.Clear();
				PaintContext.State = new();
				draw();
				var expected = layer.Instances.ToArray();
				Assert.IsTrue( expected.Length > 0 );
				layer.Clear();
				PaintFill = Color.Blue;
				PaintStroke = Stroke.Solid( Color.Green, 10 );
				PaintTextStyle = new TextStyle { FontSize = 40, Color = Color.Blue };
				PaintOpacity = opacity;
				PaintBlendMode = BlendMode.Multiply;
				Paint.Clip( new Rect( 0, 0, 1, 1 ) );
				Paint.Translate( 500, 600 );
				if ( collapsed ) Paint.Scale( 0 );
				var state = PaintContext.State;
				draw();
				Assert.AreEqual( expected.Length, layer.Instances.Count );

				for ( int i = 0; i < expected.Length; i++ )
				{
					var actual = layer.Instances[i];
					Assert.AreEqual( expected[i].GPU, actual.GPU );
					Assert.AreEqual( Matrix.Identity, actual.Transform );
					Assert.AreEqual( -1, actual.GPU.ScissorIndex );
					Assert.AreSame( expected[i].BackgroundImage, actual.BackgroundImage );
				}

				Assert.AreEqual( state, PaintContext.State );
			}
		} );
	}

	[TestMethod]
	public void LegacyTexturePreservesTransparency()
	{
		WithBuffer( layer =>
		{
			var tint = Color.Red.WithAlpha( 0.5f );
			Panel.Draw.Texture( Texture.White, new Rect( 0, 0, 32, 32 ), tint );
			var instance = layer.Instances.Single();
			Assert.AreEqual( Color.Transparent, instance.GPU.Color );
			Assert.AreEqual( tint, instance.GPU.BackgroundTint );
			Assert.AreSame( Texture.White, instance.BackgroundImage );
		} );
	}

	/// <summary>Existing signatures keep their explicit colors and do not inherit or overwrite the new drawing state.</summary>
	[TestMethod]
	public void LegacyShapeOverloadsPreservePaintAndState()
	{
		WithBuffer( layer =>
		{
			// Typed delegates also check the exact signatures used by existing compiled callers.
			Action<Rect, Color, float> rectangle = Panel.Draw.Rect;
			Action<Rect, Color, Vector4> roundedRectangle = Panel.Draw.Rect;
			Action<Vector2, float, Color> circle = Panel.Draw.Circle;
			var rect = new Rect( 10, 20, 60, 40 );
			var color = Color.Red.WithAlpha( 0.8f );
			var fill = Fill.Solid( Color.Green );
			var stroke = Stroke.Dotted( Color.Blue, 4, 3, 2 );
			PaintFill = fill;
			PaintStroke = stroke;
			PaintContext.InheritedOpacity = 0.5f;
			PaintContext.State.OverrideBlendMode = BlendMode.Multiply;
			Action[] draws = [
				() => Panel.Draw.Rect( rect, color ),
				() => Panel.Draw.Rect( rect: rect, color: color, cornerRadius: 8 ),
				() => rectangle( rect, color, 8 ),
				() => roundedRectangle( rect, color, new Vector4( 1, 2, 3, 4 ) ),
				() => circle( rect.Center, 20, color )];
			Vector4[] radii = [default, new( 8 ), new( 8 ), new( 4, 2, 3, 1 ), new( 20 )];
			for ( int i = 0; i < draws.Length; i++ )
			{
				layer.Clear();
				draws[i]();
				var instance = layer.Instances.Single();
				Assert.AreEqual( color, instance.GPU.Color );
				Assert.AreEqual( radii[i], instance.GPU.BorderRadius );
				Assert.AreEqual( 0, instance.GPU.Mode );
				Assert.IsNull( instance.PathData );
				Assert.AreEqual( BlendMode.Normal, layer.BlendMode );
				Assert.AreEqual( fill, PaintFill );
				Assert.AreEqual( stroke, PaintStroke );
				Paint.Rect( rect );
				Assert.AreEqual( 3, layer.Instances.Count );
				Assert.AreEqual( Color.Green.WithAlpha( 0.5f ), layer.Instances[1].GPU.Color );
				Assert.AreEqual( Color.Blue.WithAlpha( 0.5f ), layer.Instances[2].GPU.Color );
			}
		} );
	}

	/// <summary>The original outline overload retains analytic outline geometry and leaves the current paint untouched.</summary>
	[TestMethod]
	public void LegacyOutlinePreservesGeometryAndState()
	{
		WithBuffer( layer =>
		{
			Action<Rect, Color, float, float, float> outline = Panel.Draw.Outline;
			var rect = new Rect( 10, 20, 60, 40 );
			var color = Color.Red.WithAlpha( 0.8f );
			var fill = Fill.Solid( Color.Green );
			var stroke = Stroke.Dotted( Color.Blue, 12 );
			PaintFill = fill;
			PaintStroke = stroke;
			PaintContext.InheritedOpacity = 0.5f;
			PaintContext.State.OverrideBlendMode = BlendMode.Multiply;
			foreach ( float offset in new[] { -6f, 0f, 6f } )
			{
				layer.Clear();
				outline( rect, color, 4, 8, offset );
				var instance = layer.Instances.Single();
				Assert.AreEqual( color, instance.GPU.Color );
				Assert.AreEqual( new Vector4( 60, 40, 4, offset ), instance.GPU.BackgroundRect );
				Assert.AreEqual( new Vector4( 8 ), instance.GPU.BorderRadius );
				Assert.AreEqual( 3, instance.GPU.Mode );
				Assert.IsNull( instance.PathData );
				Assert.AreEqual( BlendMode.Normal, layer.BlendMode );
				Assert.AreEqual( fill, PaintFill );
				Assert.AreEqual( stroke, PaintStroke );
			}
			layer.Clear();
			Panel.Draw.Outline( rect: rect, color: color, width: 4 );
			Assert.AreEqual( new Vector4( 60, 40, 4, 0 ), layer.Instances.Single().GPU.BackgroundRect );
			Assert.AreEqual( Vector4.Zero, layer.Instances.Single().GPU.BorderRadius );
		} );
	}
}

#pragma warning restore CS0618
