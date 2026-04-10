using Sandbox.Rendering;

namespace Sandbox.UI;

internal partial class PanelRenderer
{
	private void BuildCommandList_Backdrop( Panel panel, ref RenderState state, CommandList commandList )
	{

		var style = panel.ComputedStyle;
		if ( style == null ) return;
		if ( !panel.HasBackdropFilter ) return;

		var attributes = commandList.Attributes;

		attributes.Set( "HasInverseScissor", 0 );

		var rect = panel.Box.Rect;
		var opacity = panel.Opacity * state.RenderOpacity;
		var size = (rect.Width + rect.Height) * 0.5f;
		var color = Color.White.WithAlpha( opacity );

		var isLayered = LayerStack?.Count > 0;

		attributes.SetCombo( "D_LAYERED", isLayered ? 1 : 0 );

		attributes.Set( "BoxPosition", panel.Box.Rect.Position );
		attributes.Set( "BoxSize", panel.Box.Rect.Size );
		SetBorderRadius( attributes, style, size );

		attributes.Set( "Brightness", style.BackdropFilterBrightness.Value.GetPixels( 1.0f ) );
		attributes.Set( "Contrast", style.BackdropFilterContrast.Value.GetPixels( 1.0f ) );
		attributes.Set( "Saturate", style.BackdropFilterSaturate.Value.GetPixels( 1.0f ) );
		attributes.Set( "Sepia", style.BackdropFilterSepia.Value.GetPixels( 1.0f ) );
		attributes.Set( "Invert", style.BackdropFilterInvert.Value.GetPixels( 1.0f ) );
		attributes.Set( "HueRotate", style.BackdropFilterHueRotate.Value.GetPixels( 1.0f ) );

		var blurScale = style.BackdropFilterBlur.Value.GetPixels( 1.0f );
		attributes.Set( "BlurScale", blurScale );

		attributes.SetCombo( "D_BLENDMODE", OverrideBlendMode );

		if ( blurScale > 0 )
		{
			// Only generate the mip levels the shader will actually sample from.
			// The shader reads at MIP level sqrt(BlurScale / 2) with trilinear filtering.
			// Round up to even for render target pool stability.
			int needed = (int)Math.Ceiling( Math.Sqrt( blurScale / 2.0f ) ) + 2;
			int maxMips = ((needed + 1) / 2) * 2;
			attributes.GrabFrameTexture( "FrameBufferCopyTexture", Graphics.DownsampleMethod.GaussianBlur, maxMips );
		}
		else
		{
			attributes.GrabFrameTexture( "FrameBufferCopyTexture", Graphics.DownsampleMethod.None );
		}

		commandList.DrawQuad( rect, Material.UI.BackdropFilter, color );
	}
}
