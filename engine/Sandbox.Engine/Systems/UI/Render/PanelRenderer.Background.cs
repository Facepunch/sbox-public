using Sandbox.Rendering;

namespace Sandbox.UI;

partial class PanelRenderer
{
	private void AddBackgroundDescriptor( Panel panel, ref RenderState state, RenderLayer target )
	{
		if ( panel.HasBackground && panel.ComputedStyle is { } style )
		{
			panel.BackgroundBlendMode = ParseBlendMode( style.BackgroundBlendMode );

			var opacity = state.RenderOpacity;
			var desc = CreateBoxDescriptor( panel, style, opacity );
			desc.BackgroundBlendMode = panel.BackgroundBlendMode;
			desc.BackgroundGradient = style.BackgroundGradient;

			var texture = style.BackgroundImage;
			if ( texture is not null && texture != Texture.Invalid )
			{
				desc.BackgroundImage = texture;
				desc.BackgroundRect = ImageRect.Calculate( new ImageRect.Input
				{
					ScaleToScreen = panel.ScaleToScreen,
					Image = texture,
					PanelRect = panel.Box.Rect,
					DefaultSize = Length.Auto,
					ImagePositionX = style.BackgroundPositionX,
					ImagePositionY = style.BackgroundPositionY,
					ImageSizeX = style.BackgroundSizeX,
					ImageSizeY = style.BackgroundSizeY,
				} ).Rect;

				if ( texture.IsDirty )
					texture.IsDirty = false;
			}
			else if ( desc.HasGradient )
			{
				// Gradients have no intrinsic size - background-size/position apply to
				// their tile like the web, sized to the panel by default.
				desc.BackgroundRect = ImageRect.Calculate( new ImageRect.Input
				{
					ScaleToScreen = panel.ScaleToScreen,
					Image = null,
					PanelRect = panel.Box.Rect,
					DefaultSize = Length.Auto,
					ImagePositionX = style.BackgroundPositionX,
					ImagePositionY = style.BackgroundPositionY,
					ImageSizeX = style.BackgroundSizeX,
					ImageSizeY = style.BackgroundSizeY,
				} ).Rect;
			}

			if ( desc.BackgroundClip == BackgroundClip.Text )
			{
				AddTextClippedBackground( panel, desc, target );
				return;
			}

			target.AddBox( desc );
		}
	}

	/// <summary>
	/// background-clip: text - the background is painted once per label under the panel, each clipped to
	/// that label's glyphs. The border isn't clipped, so it's drawn on its own and left off the copies.
	/// </summary>
	private void AddTextClippedBackground( Panel panel, BoxDrawDescriptor desc, RenderLayer target )
	{
		if ( desc.HasBorderImage || desc.BorderSize != Vector4.Zero )
			target.AddBox( desc with { BackgroundClip = BackgroundClip.BorderBox, Color = Color.Transparent, BackgroundImage = null, BackgroundGradient = default } );

		desc.BorderSize = Vector4.Zero;
		desc.BorderImageTexture = null;

		foreach ( var label in panel.Descendants.Prepend( panel ).OfType<Label>() )
		{
			if ( !label.IsVisible ) continue;
			if ( !label.GetTextMask( out var texture, out var rect ) ) continue;

			desc.TextMask = texture;
			desc.TextMaskRect = new Vector4( rect.Left - desc.PanelRect.Left, rect.Top - desc.PanelRect.Top, rect.Width, rect.Height );

			target.AddBox( desc );
		}
	}
}
