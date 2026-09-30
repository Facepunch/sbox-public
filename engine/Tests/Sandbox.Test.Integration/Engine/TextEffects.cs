using System.Collections.Generic;
using Sandbox.UI;
using SkiaSharp;
using Topten.RichTextKit;

namespace EngineTests;

[TestClass]
public class TextEffectsTests
{
	[DataTestMethod]
	[DataRow( 0f )]
	[DataRow( 4f )]
	public void ShadowIsOneTranslatedCopyOfTheGlyphs( float blur )
	{
		var original = BuildShadow( 0, 0, blur );
		var translated = BuildShadow( 12, 7, blur );
		Assert.IsTrue( original.Length > 0 );
		Assert.AreEqual( original.Length, translated.Length );
		for ( int i = 0; i < original.Length; i++ )
		{
			Assert.AreEqual( original[i].Rect + new Vector4( 12, 7, 0, 0 ), translated[i].Rect );
			Assert.AreEqual( original[i].Flags, translated[i].Flags );
			Assert.AreEqual( original[i].Color, translated[i].Color );
		}
	}

	static GPUBoxInstance[] BuildShadow( float x, float y, float blur )
	{
		var block = new Topten.RichTextKit.TextBlock { FontMapper = FontManager.Instance };
		try
		{
			var style = new Style { FontFamily = "Arial", FontSize = 32, TextColor = SKColors.Transparent };
			style.AddEffect( TextEffect.DropShadow( SKColors.White, x, y, blur ) );
			block.AddText( "Hi", style );
			var glyphs = new List<GPUBoxInstance>();
			GpuFontText.Build( block, new Vector2( 40, 30 ), GpuFontText.Options.Default, glyphs );
			return glyphs.Where( x => x.Color.a > 0 ).ToArray();
		}
		finally { block.Clear(); }
	}
}
