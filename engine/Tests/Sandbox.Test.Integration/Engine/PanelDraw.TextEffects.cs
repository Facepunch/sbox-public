using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTextTest : PainterTestBase
{
	[TestMethod]
	public void TextEffectsScaleMeasureAndReuseGlyphsWhenFaded()
	{
		WithBuffer( layer =>
		{
			foreach ( float scale in new[] { 1f, 2f } )
			{
				layer.Clear();
				PaintContext.ScaleToScreen = scale;
				var original = TextStyle.Default.WithSize( 24 ).WithBold().WithAlignment( TextFlag.Center );
				PaintTextStyle = original;
				var text = $"Effects {Guid.NewGuid():N}";
				var rect = new Rect( 0, 0, 1000, 200 );
				var plain = Paint.MeasureText( text, rect.Size );
				var styled = original.WithShadow( Color.Black.WithAlpha( 0.6f ), -3, 5, blur: 4 ).WithOutline( Color.Blue, 2 );
				PaintTextStyle = styled;
				var settings = styled.CreateScope( text, scale );
				Assert.AreEqual( 4 * scale, settings.Shadow.Size );
				Assert.AreEqual( new Vector2( -3, 5 ) * scale, settings.Shadow.Offset );
				Assert.AreEqual( 2 * scale, settings.Outline.Size );
				var block = TextRendering.GetOrCreateTextBlock( settings, styled.Alignment, rect.Size );
				var measured = Paint.MeasureText( text, rect );
				Assert.IsNull( block.Texture );
				Assert.AreEqual( plain.x + 24 * scale, measured.Width, 0.001f, "Shadow bounds include three sigma on both sides." );
				Assert.AreEqual( plain.y + 24 * scale, measured.Height, 0.001f );
				Paint.Text( text, rect );
				var drawn = layer.Instances.ToArray();
				Assert.IsTrue( drawn.Length > 0 );
				Assert.IsNull( block.Texture );
				PaintTextStyle = styled.WithColor( Color.White.WithAlpha( 0.5f ) );
				PaintOpacity = 0.5f;
				Paint.Text( text, rect );
				Assert.AreEqual( drawn.Length * 2, layer.Instances.Count );
				for ( int i = 0; i < drawn.Length; i++ )
				{
					Assert.AreEqual( drawn[i].GPU.Rect, layer.Instances[drawn.Length + i].GPU.Rect );
					Assert.AreEqual( drawn[i].GPU.Color.a * 0.25f, layer.Instances[drawn.Length + i].GPU.Color.a, 0.001f );
				}
				PaintOpacity = 1;
				PaintTextStyle = styled.WithoutShadow().WithoutOutline();
				Assert.AreEqual( plain, Paint.MeasureText( text, rect.Size ) );
			}
		} );
	}
}
