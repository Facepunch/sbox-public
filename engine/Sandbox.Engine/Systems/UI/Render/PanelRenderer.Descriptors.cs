using Sandbox.Rendering;

namespace Sandbox.UI;

internal partial class PanelRenderer
{
	BoxDrawDescriptor CreateBoxDescriptor( Panel panel, Styles style, float opacity )
	{
		var rect = panel.Box.Rect;
		var size = (rect.Width + rect.Height) * 0.5f;

		var color = style.BackgroundColor.Value;
		color.a *= opacity;

		var desc = new BoxDrawDescriptor( rect, color )
		{
			BorderRadius = new Vector4(
				style.BorderBottomRightRadius.Value.GetPixels( size ),
				style.BorderTopRightRadius.Value.GetPixels( size ),
				style.BorderBottomLeftRadius.Value.GetPixels( size ),
				style.BorderTopLeftRadius.Value.GetPixels( size )
			),
			BorderSize = new Vector4(
				style.BorderLeftWidth.Value.GetPixels( size ),
				style.BorderTopWidth.Value.GetPixels( size ),
				style.BorderRightWidth.Value.GetPixels( size ),
				style.BorderBottomWidth.Value.GetPixels( size )
			),
			BorderColorL = style.BorderLeftColor.Value.WithAlphaMultiplied( opacity ),
			BorderColorT = style.BorderTopColor.Value.WithAlphaMultiplied( opacity ),
			BorderColorR = style.BorderRightColor.Value.WithAlphaMultiplied( opacity ),
			BorderColorB = style.BorderBottomColor.Value.WithAlphaMultiplied( opacity ),
			BackgroundTint = style.BackgroundTint.Value.WithAlphaMultiplied( opacity ),
			BackgroundRepeat = style.BackgroundRepeat ?? BackgroundRepeat.Repeat,
			BackgroundAngle = style.BackgroundAngle.Value.GetPixels( 1.0f ),
			OverrideBlendMode = OverrideBlendMode,
			FilterMode = (style.ImageRendering ?? ImageRendering.Anisotropic) switch
			{
				ImageRendering.Point => FilterMode.Point,
				ImageRendering.Bilinear => FilterMode.Bilinear,
				ImageRendering.Trilinear => FilterMode.Trilinear,
				_ => FilterMode.Anisotropic
			},
		};

		if ( style.BorderImageSource != null )
		{
			desc.BorderImageTexture = style.BorderImageSource;
			desc.BorderImageSlice = new Vector4(
				style.BorderImageWidthLeft.Value.GetPixels( size ),
				style.BorderImageWidthTop.Value.GetPixels( size ),
				style.BorderImageWidthRight.Value.GetPixels( size ),
				style.BorderImageWidthBottom.Value.GetPixels( size )
			);
			desc.BorderImageRepeat = style.BorderImageRepeat ?? BorderImageRepeat.Stretch;
			desc.BorderImageFill = style.BorderImageFill ?? BorderImageFill.Unfilled;
			desc.BorderImageTint = style.BorderImageTint.Value.WithAlphaMultiplied( opacity );
		}

		return desc;
	}

	/// <summary>
	/// Creates a box descriptor for a texture and adds it to the target RenderLayer.
	/// Used by Image, SvgPanel, ScenePanel etc.
	/// </summary>
	public void AddBackgroundTextureDescriptor( Panel panel, Texture texture, in RenderState state, Length defaultSize, RenderLayer target )
	{
		var style = panel.ComputedStyle;
		if ( style == null ) return;

		if ( texture == Texture.Invalid )
			texture = null;

		var opacity = state.RenderOpacity;
		var desc = CreateBoxDescriptor( panel, style, opacity );

		if ( texture != null )
		{
			desc.BackgroundImage = texture;
			desc.BackgroundRect = ImageRect.Calculate( new ImageRect.Input
			{
				ScaleToScreen = panel.ScaleToScreen,
				Image = texture,
				PanelRect = panel.Box.Rect,
				DefaultSize = defaultSize,
				ImagePositionX = style.BackgroundPositionX,
				ImagePositionY = style.BackgroundPositionY,
				ImageSizeX = style.BackgroundSizeX,
				ImageSizeY = style.BackgroundSizeY,
			} ).Rect;
		}

		target.Boxes.Add( desc );
	}

	/// <summary>
	/// Rebuild all descriptors for a panel. Called during build phase when panel is dirty.
	/// </summary>
	private void RebuildDescriptors( Panel panel, ref RenderState state )
	{
		var target = panel.CachedDescriptors;
		target.Clear();

		target.Scissor = ScissorGPU;

		AddShadowDescriptors( panel, ref state, inset: false, target );
		panel.PushLayer( this );
		AddBackdropDescriptor( panel, ref state, target );
		AddBackgroundDescriptor( panel, ref state, target );

		// Clear dirty flag before Draw() so panel.MarkRenderDirty() calls inside
		// Draw() (e.g. TextEntry caret blink) can re-mark for next frame.
		panel.IsRenderDirty = false;

		var drawBuffer = UIDrawBuffer.Current;
		drawBuffer.ActiveLayer = target;

		try
		{
			panel.OnDraw();
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}

		drawBuffer.ActiveLayer = null;

		AddShadowDescriptors( panel, ref state, inset: true, target );
		AddOutlineDescriptor( panel, ref state, target );
	}
}
