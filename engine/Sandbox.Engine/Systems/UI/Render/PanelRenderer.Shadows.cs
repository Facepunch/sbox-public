namespace Sandbox.UI;

partial class PanelRenderer
{
	private void AddShadowToCommandList( Panel panel, ref RenderState state, in Shadow shadow )
	{
		if ( shadow.Color.a <= 0 )
			return;

		var attributes = panel.CommandList.Attributes;
		attributes.Set( "HasInverseScissor", 0 );
		panel.CommandList.InsertList( panel.ClipCommandList );

		var inset = shadow.Inset;
		var style = panel.ComputedStyle;
		var rect = panel.Box.Rect;
		var size = (rect.Width + rect.Height) * 0.5f;
		var shadowOffset = new Vector2( shadow.OffsetX, shadow.OffsetY );
		var shadowRect = inset ? rect : rect + shadowOffset;

		var blur = shadow.Blur;
		var spread = shadow.Spread;
		var borderRadius = new Vector4(
			style.BorderTopLeftRadius.Value.GetPixels( size ),
			style.BorderTopRightRadius.Value.GetPixels( size ),
			style.BorderBottomLeftRadius.Value.GetPixels( size ),
			style.BorderBottomRightRadius.Value.GetPixels( size )
		);

		shadowRect = shadowRect.Grow( spread );

		var opacity = panel.Opacity * state.RenderOpacity;
		var color = shadow.Color;
		color.a *= opacity;

		attributes.Set( "BoxPosition", new Vector2( shadowRect.Left, shadowRect.Top ) );
		attributes.Set( "BoxSize", new Vector2( shadowRect.Width, shadowRect.Height ) );
		attributes.Set( "BorderRadius", borderRadius );
		attributes.Set( "ShadowWidth", blur );
		attributes.Set( "ShadowOffset", shadowOffset );
		attributes.Set( "Bloat", blur );
		attributes.Set( "Inset", inset );
		attributes.SetCombo( "D_BLENDMODE", OverrideBlendMode );

		if ( inset )
		{
			// Inset shadows appear inside the panel, so we clip for that
			attributes.Set( "ScissorRect", panel.Box.ClipRect.ToVector4() );
			attributes.Set( "ScissorCornerRadius", borderRadius );
			attributes.Set( "ScissorTransformMat", panel.GlobalMatrix ?? Matrix.Identity );
			attributes.Set( "HasScissor", 1 );
		}
		else
		{
			// Normal/outset shadows appear outside the panel
			attributes.Set( "InverseScissorRect", panel.Box.ClipRect.ToVector4() );
			attributes.Set( "InverseScissorCornerRadius", borderRadius );
			attributes.Set( "InverseScissorTransformMat", panel.GlobalMatrix ?? Matrix.Identity );
			attributes.Set( "HasInverseScissor", 1 );
		}

		panel.CommandList.DrawQuad( shadowRect.Grow( blur ), Material.UI.BoxShadow, color );
	}

	internal void BuildCommandList_BoxShadows( Panel panel, ref RenderState state, bool inset )
	{
		ThreadSafe.AssertIsMainThread();

		var shadows = panel.ComputedStyle.BoxShadow;
		var c = shadows.Count;

		if ( c == 0 )
			return;

		for ( int i = 0; i < c; i++ )
		{
			if ( shadows[i].Inset != inset )
				continue;

			AddShadowToCommandList( panel, ref state, shadows[i] );
		}
	}
}
