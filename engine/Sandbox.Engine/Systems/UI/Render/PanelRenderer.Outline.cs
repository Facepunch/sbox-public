namespace Sandbox.UI;

partial class PanelRenderer
{
	internal void AddOutlineDescriptor( Panel panel, ref RenderState state, RenderLayer target )
	{
		ThreadSafe.AssertIsMainThread();

		var style = panel.ComputedStyle;
		if ( style == null ) return;

		var outlineColor = style.OutlineColor.Value;
		if ( outlineColor.a <= 0 ) return;

		var rect = panel.Box.Rect;
		var size = (rect.Width + rect.Height) * 0.5f;
		var outlineWidth = style.OutlineWidth.Value.GetPixels( size );

		if ( outlineWidth <= 0 ) return;

		var opacity = state.RenderOpacity;
		var color = outlineColor;
		color.a *= opacity;

		target.Outlines.Add( new OutlineDrawDescriptor( rect, color, outlineWidth )
		{
			BorderRadius = new Vector4(
				style.BorderTopLeftRadius.Value.GetPixels( size ),
				style.BorderTopRightRadius.Value.GetPixels( size ),
				style.BorderBottomLeftRadius.Value.GetPixels( size ),
				style.BorderBottomRightRadius.Value.GetPixels( size )
			),
			Offset = style.OutlineOffset.Value.GetPixels( size ),
			OverrideBlendMode = OverrideBlendMode,
		} );
	}
}
