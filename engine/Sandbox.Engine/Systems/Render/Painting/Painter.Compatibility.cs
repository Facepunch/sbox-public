using Sandbox.UI;

namespace Sandbox;

public readonly ref partial struct Painter
{
	// Legacy commands use layout coordinates and explicit paint settings. They do not read
	// mutable Painter state; panel transforms and clipping are applied during playback.
	internal void LegacyText( string text, Rect rect, TextStyle style )
		=> DrawText( text, rect, style, legacy: true );

	internal void LegacyRect( Rect rect, Color color, Vector4 cornerRadius )
		=> Resolved( new BoxDescriptor( rect, color ) { BorderRadius = cornerRadius } );

	internal void LegacyTexture( Texture texture, Rect rect, Color tint )
		=> Resolved( new BoxDescriptor( rect, Color.Transparent )
		{
			BackgroundImage = texture,
			BackgroundTint = tint,
			BackgroundRepeat = BackgroundRepeat.Clamp,
		} );

	internal void LegacyShadow( Rect rect, Color color, float blur, float spread, Vector2 offset, float cornerRadius, bool inset )
		=> GetActiveContext().Batcher.Add( new ShadowDescriptor( rect, color )
		{
			BorderRadius = new Vector4( cornerRadius ),
			Blur = blur,
			Spread = spread,
			Offset = offset,
			Inset = inset,
		} );

	internal void LegacyOutline( Rect rect, Color color, float width, float cornerRadius, float offset )
		=> GetActiveContext().Batcher.Add( new OutlineDescriptor( rect, color, width )
		{
			BorderRadius = new Vector4( cornerRadius ),
			Offset = offset,
		} );
}
