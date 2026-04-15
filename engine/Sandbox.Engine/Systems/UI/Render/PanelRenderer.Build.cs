namespace Sandbox.UI;

internal partial class PanelRenderer
{
	internal void BuildDescriptors( Panel panel, RenderState state )
	{
		if ( panel?.ComputedStyle == null || !panel.IsVisible )
			return;

		BuildTransformState( panel );
		UpdateScissorState( panel );

		var renderMode = PushRenderMode( panel );
		panel.CachedOverrideBlendMode = OverrideBlendMode;

		panel.UpdateLayer( panel.ComputedStyle );
		UpdateBackgroundImageState( panel );

		// Accumulate CSS opacity into state so children inherit it
		// without depending on layout having updated panel.Opacity.
		state = state with { RenderOpacity = state.RenderOpacity * (panel.ComputedStyle?.Opacity ?? 1.0f) };

		if ( MathF.Abs( panel.CachedRenderOpacity - state.RenderOpacity ) > 0.001f )
		{
			panel.CachedRenderOpacity = state.RenderOpacity;
			panel.IsRenderDirty = true;
		}

		if ( panel.IsRenderDirty || panel.HasPanelLayer )
		{
			panel.CachedDescriptors ??= RenderLayer.Rent();
			RebuildDescriptors( panel, ref state );
		}

		panel.CachedRenderMode = GetRenderMode( panel );

		if ( panel.HasChildren )
			panel.BuildDescriptorsForChildren( this, ref state );

		if ( panel.HasPanelLayer )
			panel.BuildLayerPopCommands( this, DefaultRenderTarget );

		if ( renderMode ) PopRenderMode();
	}

	Panel.RenderMode GetRenderMode( Panel panel )
	{
		if ( panel.HasPanelLayer )
			return Panel.RenderMode.Layer;

		if ( panel.HasBackdropFilter ) return Panel.RenderMode.Inline;
		if ( panel is ScenePanel ) return Panel.RenderMode.Inline;
		if ( panel is BasePopup ) return Panel.RenderMode.Inline;

		return Panel.RenderMode.Batched;
	}

	void UpdateScissorState( Panel panel )
	{
		var hash = HashCode.Combine( ScissorGPU.Rect, ScissorGPU.CornerRadius, ScissorGPU.Matrix );
		if ( panel._lastScissorHash == hash ) return;

		panel._lastScissorHash = hash;
		panel.CachedDescriptors ??= new();
		panel.CachedDescriptors.Scissor = ScissorGPU;
	}

	void UpdateBackgroundImageState( Panel panel )
	{
		if ( panel.ComputedStyle?.BackgroundImage is not { } tex )
			return;

		if ( tex.IsDirty )
			panel.IsRenderDirty = true;

		tex.MarkUsed();
	}
}
