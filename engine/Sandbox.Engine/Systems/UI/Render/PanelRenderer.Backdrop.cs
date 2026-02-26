namespace Sandbox.UI;

internal partial class PanelRenderer
{
	private void BuildCommandList_Backdrop( Panel panel, ref RenderState state )
	{
		ThreadSafe.AssertIsMainThread();

		var style = panel.ComputedStyle;
		if ( style == null ) return;
		if ( !panel.HasBackdropFilter ) return;

		var attributes = panel.CommandList.Attributes;

		attributes.Set( "HasInverseScissor", 0 );
		panel.CommandList.InsertList( panel.ClipCommandList );

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
		attributes.Set( "BlurScale", style.BackdropFilterBlur.Value.GetPixels( 1.0f ) );

		attributes.SetCombo( "D_BLENDMODE", OverrideBlendMode );

		attributes.GrabFrameTexture( "FrameBufferCopyTexture", Graphics.DownsampleMethod.GaussianBlur );
		panel.CommandList.DrawQuad( rect, Material.UI.BackdropFilter, color );
	}
}
