using Sandbox.Rendering;

namespace Sandbox.UI;

internal partial class PanelRenderer
{
	private void AddBackdropDescriptor( Panel panel, ref RenderState state, RenderLayer target )
	{
		var style = panel.ComputedStyle;
		if ( style == null ) return;
		if ( !panel.HasBackdropFilter ) return;

		var rect = panel.Box.Rect;
		var opacity = state.RenderOpacity;
		var size = (rect.Width + rect.Height) * 0.5f;

		target.Backdrops.Add( new BackdropDrawDescriptor( rect )
		{
			BorderRadius = new Vector4(
				style.BorderBottomRightRadius.Value.GetPixels( size ),
				style.BorderTopRightRadius.Value.GetPixels( size ),
				style.BorderBottomLeftRadius.Value.GetPixels( size ),
				style.BorderTopLeftRadius.Value.GetPixels( size )
			),
			Opacity = opacity,
			Brightness = style.BackdropFilterBrightness.Value.GetPixels( 1.0f ),
			Contrast = style.BackdropFilterContrast.Value.GetPixels( 1.0f ),
			Saturate = style.BackdropFilterSaturate.Value.GetPixels( 1.0f ),
			Sepia = style.BackdropFilterSepia.Value.GetPixels( 1.0f ),
			Invert = style.BackdropFilterInvert.Value.GetPixels( 1.0f ),
			HueRotate = style.BackdropFilterHueRotate.Value.GetPixels( 1.0f ),
			BlurScale = style.BackdropFilterBlur.Value.GetPixels( 1.0f ),
			OverrideBlendMode = OverrideBlendMode,
			IsLayered = LayerStack?.Count > 0,
		} );
	}
}
