using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	[TestMethod]
	public void DrawsCaptureOpacity()
	{
		WithBuffer( layer =>
		{
			using var texture = Texture.Create( 8, 4 ).Finish();
			var rect = new Rect( 0, 0, 100, 40 );
			var color = Color.Red.WithAlpha( 0.8f );
			var gradient = Fill.LinearGradient( Color.Red, Color.Blue );
			PaintContext.InheritedOpacity = 0.5f;
			void Draw()
			{
				PaintFill = color;
				PaintStroke = Stroke.Solid( color, 2 );
				Paint.Rect( rect );
				PaintFill = gradient;
				PaintStroke = Stroke.None;
				Paint.Rect( rect );
				PaintFill = Fill.Image( texture, color );
				Paint.Rect( rect );
				Paint.Texture( texture, rect, color );
				PaintFill = color;
				Paint.Rect( rect );
				PaintStroke = Stroke.Solid( color, 2 );
				Paint.Outline( rect );
				Paint.RectShadow( rect, color: color, blur: 5 );
				PaintTextStyle = new TextStyle { FontSize = 14, Color = color };
				Paint.Text( "Opacity test", rect );
			}
			Draw();
			var original = layer.Instances.ToArray();
			layer.Clear();
			using ( Paint.Scope() )
			{
				PaintOpacity = 0.5f;
				using ( Paint.Scope() )
				{
					PaintOpacity *= 0.5f;
					Draw();
				}
				Assert.AreEqual( 0.5f, PaintOpacity );
			}
			Assert.AreEqual( 1f, PaintOpacity );
			Assert.AreEqual( original.Length, layer.Instances.Count );
			for ( int i = 0; i < original.Length; i++ )
			{
				var expected = original[i].GPU;
				var actual = layer.Instances[i].GPU;
				Assert.AreEqual( expected.Color.WithAlphaMultiplied( 0.25f ), actual.Color, $"Draw {i}" );
				if ( original[i].BackgroundImage is not null || original[i].BackgroundGradient.Count > 0 )
					Assert.AreEqual( expected.BackgroundTint.WithAlphaMultiplied( 0.25f ), actual.BackgroundTint, $"Background {i}" );
				Assert.AreEqual( expected.Rect, actual.Rect );
				Assert.AreSame( original[i].BackgroundImage, layer.Instances[i].BackgroundImage );
			}
			PaintOpacity = 0;
			layer.Clear();
			Draw();
			Assert.AreEqual( 0, layer.Instances.Count );
		} );
	}
}
