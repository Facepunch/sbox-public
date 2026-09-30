using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTextTest : PainterTestBase
{
	[TestMethod]
	public void MeasurementDoesNotRasterize()
	{
		WithBuffer( layer =>
		{
			foreach ( float scale in new[] { 1f, 2f } )
				foreach ( var alignment in new[] { TextFlag.LeftTop, TextFlag.Center, TextFlag.RightBottom } )
				{
					layer.Clear();
					PaintContext.ScaleToScreen = scale;
					PaintTextStyle = new TextStyle { FontSize = 16, FontWeight = 700, Italic = true, LetterSpacing = -0.5f, Alignment = alignment };
					var text = $"Measured fj Á {Guid.NewGuid():N}";
					var rect = new Rect( 10.25f, 20.75f, 180, 160 );
					var block = TextRendering.GetOrCreateTextBlock( PaintTextStyle.CreateScope( text, scale ), alignment, rect.Size );
					Assert.IsNull( block.Texture );
					var state = PaintContext.State;
					var measured = Paint.MeasureText( text, rect );
					var size = Paint.MeasureText( text, rect.Size );
					Assert.IsTrue( size.x > 0 && size.y > 0 );
					Assert.AreEqual( size, measured.Size );
					Assert.IsNull( block.Texture, "Measuring must not rasterize text or allocate a GPU texture." );
					Assert.AreEqual( 0, layer.Instances.Count );
					Assert.AreEqual( state, PaintContext.State );
					Paint.Translate( 100, 200 );
					Paint.Rotate( 30 );
					Paint.Clip( new Rect( 0, 0, 5, 5 ) );
					Assert.AreEqual( measured, Paint.MeasureText( text, rect ), "Transform and clipping do not change text layout." );
					Paint.Text( text, rect );
					Assert.IsNull( block.Texture, "Drawing evaluates GPU glyph outlines without rasterizing a texture." );
					Assert.IsTrue( layer.Instances.Count > 0 );
					Assert.AreEqual( size, block.Size );
					Assert.AreEqual( size, Paint.MeasureText( text, rect.Size ) );
					PaintContext.State = state;
				}
		} );
	}

	[TestMethod]
	public void MeasurementReusesWrappedLayout()
	{
		WithBuffer( layer =>
		{
			PaintTextStyle = new TextStyle { FontSize = 18 };
			var text = $"Measured words wrap onto multiple lines. {Guid.NewGuid():N}";
			var wide = Paint.MeasureText( text );
			var narrow = Paint.MeasureText( text, new Vector2( 150, 1000 ) );
			Assert.IsTrue( wide.x > narrow.x );
			Assert.IsTrue( narrow.y > wide.y );
			PaintOpacity = 0;
			Assert.AreEqual( wide, Paint.MeasureText( text ) );
			Assert.AreEqual( Vector2.Zero, Paint.MeasureText( "" ) );
			Assert.AreEqual( Vector2.Zero, Paint.MeasureText( null ) );
			Assert.AreEqual( new Rect( 10, 20, 0, 0 ), Paint.MeasureText( "", new Rect( 10, 20, 100, 100 ) ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => Paint.MeasureText( text, new Vector2( float.NaN, 100 ) ) );
			PaintOpacity = 1;
			Paint.Text( "", new Rect( 0, 0, 100, 100 ) );
			Assert.AreEqual( 0, layer.Instances.Count );
		} );
	}
}
