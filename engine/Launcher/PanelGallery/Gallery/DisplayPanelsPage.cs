namespace Sandbox.PanelGallery;

/// <summary>
/// The panels that show something rather than edit something. Image draws a texture, SvgPanel
/// rasterises vector art at the size it ends up - each goes through its own draw path, so a
/// rendering break in either shows up here.
/// </summary>
public class DisplayPanelsPage : GalleryPage
{
	public DisplayPanelsPage() : base( "Images", "Sandbox.UI.Image. Compare native-sized textures and stretched images." )
	{
		// Built here rather than loaded, so the page tests the panel and not whether an asset
		// resolved. The checks make filtering and stretching obvious
		var checker = Checkerboard();

		var row = Case( "Image" );
		row.AddChild( new Sandbox.UI.Image { Texture = checker } );
		row.AddChild( new Sandbox.UI.Image { Texture = Texture.White } );

		// Same texture, stretched - the sampler's filtering is the thing to look at
		row = Case( "Image, stretched" );
		var stretched = new Sandbox.UI.Image { Texture = checker };
		stretched.Style.Width = 200;
		stretched.Style.Height = 60;
		row.AddChild( stretched );

		Output().Text = "The checks are drawn in code - a blank square means Image is broken, not a missing file.";
	}

	/// <summary>
	/// An 8x8 checkerboard, so scaling and filtering are easy to see.
	/// </summary>
	internal static Texture Checkerboard()
	{
		const int size = 8;
		var data = new byte[size * size * 4];

		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				var light = (x + y) % 2 == 0;
				var i = (y * size + x) * 4;

				data[i + 0] = (byte)(light ? 220 : 60);
				data[i + 1] = (byte)(light ? 220 : 60);
				data[i + 2] = (byte)(light ? 220 : 60);
				data[i + 3] = 255;
			}
		}

		return Texture.Create( size, size ).WithData( data ).Finish();
	}
}
