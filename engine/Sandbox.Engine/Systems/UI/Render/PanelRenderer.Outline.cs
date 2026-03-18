namespace Sandbox.UI;

partial class PanelRenderer
{
	internal void BuildCommandList_Outline( Panel panel, ref RenderState state )
	{
		ThreadSafe.AssertIsMainThread();

		var style = panel.ComputedStyle;
		if ( style == null ) return;

		var outlineColor = style.OutlineColor.Value;
		if ( outlineColor.a <= 0 )
			return;

		var rect = panel.Box.Rect;
		var size = (rect.Width + rect.Height) * 0.5f;
		var outlineWidth = style.OutlineWidth.Value.GetPixels( size );

		if ( outlineWidth <= 0 )
			return;

		var outlineOffset = style.OutlineOffset.Value.GetPixels( size );

		var outwardExtent = outlineOffset + outlineWidth;
		if ( outwardExtent < 0.0f )
			outwardExtent = 0.0f;

		var bloat = outwardExtent + 1.0f;

		var quadRect = rect.Grow( bloat );

		var borderRadius = new Vector4(
			style.BorderTopLeftRadius.Value.GetPixels( size ),
			style.BorderTopRightRadius.Value.GetPixels( size ),
			style.BorderBottomLeftRadius.Value.GetPixels( size ),
			style.BorderBottomRightRadius.Value.GetPixels( size )
		);

		var opacity = panel.Opacity * state.RenderOpacity;
		var color = outlineColor;
		color.a *= opacity;

		var attributes = panel.CommandList.Attributes;
		attributes.Set( "HasInverseScissor", 0 );
		panel.CommandList.InsertList( panel.ClipCommandList );

		attributes.Set( "BoxPosition", new Vector2( quadRect.Left, quadRect.Top ) );
		attributes.Set( "BoxSize", new Vector2( quadRect.Width, quadRect.Height ) );
		attributes.Set( "PanelSize", new Vector2( rect.Width, rect.Height ) );
		attributes.Set( "BorderRadius", borderRadius );
		attributes.Set( "OutlineWidth", outlineWidth );
		attributes.Set( "OutlineOffset", outlineOffset );
		attributes.Set( "Bloat", bloat );
		attributes.SetCombo( "D_BLENDMODE", OverrideBlendMode );

		panel.CommandList.DrawQuad( quadRect, Material.UI.Outline, color );
	}
}
