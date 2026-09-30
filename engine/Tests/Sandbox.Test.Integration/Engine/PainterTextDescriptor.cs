using Sandbox.UI;

namespace EngineTests;

[TestClass]
public class PainterTextDescriptorTest : PainterTestBase
{
	[TestMethod]
	public void TextDrawingTracksPlacementOpacityAndBlend()
	{
		var root = CreateLabel( out var label );
		try
		{
			using var output = new PainterTestOutput( PaintContext.Batcher );
			var block = label._textBlock;
			var painter = Paint;
			painter.Opacity = 0;
			painter.Transform = Matrix.CreateScale( new Vector3( 0, 0, 1 ) );
			var rect = new Rect( 30, 40, 300, 100 );
			block.Draw( painter, BlendMode.Normal, label.ComputedStyle, rect, 0.75f );
			var first = PaintContext.Batcher.Instances.ToArray();
			Assert.IsTrue( first.Length > 0 );
			Assert.IsNull( block.Texture, "Labels draw GPU outlines without a raster texture." );
			Assert.IsTrue( first.All( x => x.Color.a == 0.75f ) );
			output.Clear();

			block.Draw( painter, BlendMode.Multiply, label.ComputedStyle, rect + new Vector2( 15, 25 ), 0.25f );
			var moved = PaintContext.Batcher.Instances;
			Assert.AreEqual( first.Length, moved.Count );
			for ( int i = 0; i < first.Length; i++ )
			{
				Assert.AreEqual( first[i].Rect.x + 15, moved[i].Rect.x, 0.01f );
				Assert.AreEqual( first[i].Rect.y + 25, moved[i].Rect.y, 0.01f );
				Assert.AreEqual( 0.25f, moved[i].Color.a );
			}
			Assert.AreEqual( BlendMode.Multiply, output.BlendMode );
			block.Draw( painter, BlendMode.Normal, label.ComputedStyle, rect, 0 );
			Assert.AreEqual( first.Length, moved.Count );
		}
		finally { root.Delete( true ); }
	}

	[TestMethod]
	public void TextChangesRebuildGlyphsWithoutAllocatingATexture()
	{
		var root = CreateLabel( out var label );
		try
		{
			using var output = new PainterTestOutput( PaintContext.Batcher );
			var rect = new Rect( 20, 30, 400, 120 );
			label._textBlock.Draw( Paint, BlendMode.Normal, label.ComputedStyle, rect, 1 );
			int count = output.Instances.Count;
			output.Clear();
			label.Text = "A considerably longer line of text";
			root.Layout();
			label._textBlock.Draw( Paint, BlendMode.Normal, label.ComputedStyle, rect, 1 );
			Assert.IsTrue( output.Instances.Count > count );
			Assert.IsNull( label._textBlock.Texture );
		}
		finally { root.Delete( true ); }
	}

	static RootPanel CreateLabel( out Label label )
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 800, 600 ) };
		label = root.AddChild<Label>();
		label.Style.Set( "position: absolute; width: 500px; height: 120px; font-family: Arial; font-size: 24px; color: white;" );
		label.Text = "Text";
		root.Layout();
		return root;
	}
}
