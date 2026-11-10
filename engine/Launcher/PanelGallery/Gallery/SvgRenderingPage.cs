namespace Sandbox.PanelGallery;

/// <summary>
/// Shows embedded SVG artwork through the same rasterizer used by SvgPanel.
/// </summary>
public class SvgRenderingPage : GalleryPage
{
	public SvgRenderingPage() : base( "SVG Rendering", "Full-colour vector artwork, embedded directly as SVG text and rasterized at the panel's size." )
	{
		var artwork = Add.Panel( "svg-artwork-grid" );
		AddArtwork( artwork, "Rocket", "Layered curves, metallic shading and gradients.", GallerySvgArtwork.Rocket );
		AddArtwork( artwork, "Dragon", "Intricate paths, overlapping details and colour gradients.", GallerySvgArtwork.Dragon );
		AddArtwork( artwork, "Butterfly", "Symmetrical wings, delicate markings and translucent shading.", GallerySvgArtwork.Butterfly );
		AddArtwork( artwork, "Jellyfish", "Soft gradients, curved tentacles and transparent layers.", GallerySvgArtwork.Jellyfish );
		Add.Label( "Artwork: Microsoft Fluent Emoji · MIT License · github.com/microsoft/fluentui-emoji", "page-blurb" );
		Add.Label( "Rasterization sizes", "page-title" );
		var sizes = Add.Panel( "svg-size-row" );
		foreach ( var size in new[] { 24, 48, 96, 192 } )
		{
			var item = sizes.Add.Panel( "svg-size-item" );
			var svg = new ArtworkPanel( GallerySvgArtwork.Rocket );
			svg.Style.Width = size; svg.Style.Height = size;
			item.AddChild( svg );
			item.Add.Label( $"{size}px", "page-blurb" );
		}
		Add.Label( "Tint", "page-title" );
		var tintRow = Add.Panel( "svg-tint-row" );
		var tinted = new ArtworkPanel( GallerySvgArtwork.Butterfly );
		tinted.Style.Width = 144; tinted.Style.Height = 144;
		tintRow.AddChild( tinted );
		foreach ( var (name, color) in new[] { ("Original", (string)null), ("Cyan", GalleryPalette.CyanHex), ("Pink", GalleryPalette.PinkHex), ("Lime", GalleryPalette.LimeHex) } )
			tintRow.AddChild( new Sandbox.UI.Button( name, null, "flatbutton", () => tinted.Tint = color ) );
	}

	static void AddArtwork( Panel row, string title, string note, string source )
	{
		var card = row.Add.Panel( "svg-artwork-card" );
		var stage = card.Add.Panel( "svg-artwork-stage" );
		stage.AddChild( new ArtworkPanel( source ) );
		card.Add.Label( title, "reference-title" );
		card.Add.Label( note, "reference-note" );
	}

	/// <summary>
	/// Rasterizes embedded SVG when final dimensions or tint change, retaining the cached texture between layouts.
	/// </summary>
	sealed class ArtworkPanel( string source ) : Panel
	{
		/// <summary>
		/// Hex colour the artwork is tinted with, or null for the original colours.
		/// </summary>
		public string Tint { get; set; }
		Texture _texture;
		int _lastKey;
		public override void FinalLayout( Vector2 offset )
		{
			base.FinalLayout( offset );
			int width = (int)Box.Rect.Width, height = (int)Box.Rect.Height;
			if ( width <= 0 || height <= 0 ) return;
			int key = HashCode.Combine( width, height, Tint );
			if ( key == _lastKey ) return;
			_lastKey = key;
			_texture = Texture.CreateFromSvgSource( source, width, height, Tint is null ? (Color?)null : (Color)Tint );
		}
		public override void OnDraw( Painter painter ) => DrawTexture( painter, _texture, Length.Contain );
	}
}
