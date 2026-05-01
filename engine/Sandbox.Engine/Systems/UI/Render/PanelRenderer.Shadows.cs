using Sandbox.Rendering;

namespace Sandbox.UI;

partial class PanelRenderer
{
	internal void AddShadowDescriptors( Panel panel, ref RenderState state, bool inset, RenderLayer target )
	{
		var shadows = panel.ComputedStyle.BoxShadow;
		var c = shadows.Count;

		if ( c == 0 )
			return;

		var style = panel.ComputedStyle;
		var rect = panel.Box.Rect;
		var size = (rect.Width + rect.Height) * 0.5f;
		var opacity = state.RenderOpacity;

		var borderRadius = new Vector4(
			style.BorderTopLeftRadius.Value.GetPixels( size ),
			style.BorderTopRightRadius.Value.GetPixels( size ),
			style.BorderBottomLeftRadius.Value.GetPixels( size ),
			style.BorderBottomRightRadius.Value.GetPixels( size )
		);

		var scissorRect = panel.Box.ClipRect.ToVector4();
		var scissorMat = panel.GlobalMatrix ?? Matrix.Identity;

		for ( int i = 0; i < c; i++ )
		{
			var shadow = shadows[i];
			if ( shadow.Inset != inset ) continue;
			if ( shadow.Color.a <= 0 ) continue;

			var color = shadow.Color;
			color.a *= opacity;

			target.AddShadow( new ShadowDrawDescriptor( rect, color )
			{
				BorderRadius = borderRadius,
				Offset = new Vector2( shadow.OffsetX, shadow.OffsetY ),
				Blur = shadow.Blur,
				Spread = shadow.Spread,
				Inset = inset,
				OverrideBlendMode = OverrideBlendMode,
				ScissorRect = scissorRect,
				ScissorCornerRadius = borderRadius,
				ScissorTransformMat = scissorMat,
			} );
		}
	}
}
