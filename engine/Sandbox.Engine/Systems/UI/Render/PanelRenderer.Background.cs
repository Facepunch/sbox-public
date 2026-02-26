using Sandbox.Rendering;

namespace Sandbox.UI;

partial class PanelRenderer
{
	private void UpdateRenderAttributes( CommandList.AttributeAccess attributes, Panel panel )
	{
		panel.BackgroundBlendMode = ParseBlendMode( panel.ComputedStyle?.BackgroundBlendMode );

		var style = panel.ComputedStyle;
		if ( style is null ) return;

		var rect = panel.Box.Rect;
		var opacity = panel.Opacity * 1;

		var color = style.BackgroundColor.Value;
		color.a *= opacity;

		var size = (rect.Width + rect.Height) * 0.5f;

		var borderSize = new Vector4(
			style.BorderLeftWidth.Value.GetPixels( size ),
			style.BorderTopWidth.Value.GetPixels( size ),
			style.BorderRightWidth.Value.GetPixels( size ),
			style.BorderBottomWidth.Value.GetPixels( size )
		);

		var borderRadius = new Vector4(
			style.BorderBottomRightRadius.Value.GetPixels( size ),
			style.BorderTopRightRadius.Value.GetPixels( size ),
			style.BorderBottomLeftRadius.Value.GetPixels( size ),
			style.BorderTopLeftRadius.Value.GetPixels( size )
		);

		attributes.Set( "BorderRadius", borderRadius );

		if ( borderSize.x == 0 && borderSize.y == 0 && borderSize.z == 0 && borderSize.w == 0 )
		{
			attributes.Set( "HasBorder", 0 );
		}
		else
		{
			attributes.Set( "HasBorder", 1 );
			attributes.Set( "BorderSize", borderSize );

			attributes.Set( "BorderColorL", style.BorderLeftColor.Value.WithAlphaMultiplied( opacity ) );
			attributes.Set( "BorderColorT", style.BorderTopColor.Value.WithAlphaMultiplied( opacity ) );
			attributes.Set( "BorderColorR", style.BorderRightColor.Value.WithAlphaMultiplied( opacity ) );
			attributes.Set( "BorderColorB", style.BorderBottomColor.Value.WithAlphaMultiplied( opacity ) );
		}

		// We have a border image
		if ( style.BorderImageSource != null )
		{
			attributes.Set( "BorderImageTexture", style.BorderImageSource );
			attributes.Set( "BorderImageSlice", new Vector4(
				style.BorderImageWidthLeft.Value.GetPixels( size ),
				style.BorderImageWidthTop.Value.GetPixels( size ),
				style.BorderImageWidthRight.Value.GetPixels( size ),
				style.BorderImageWidthBottom.Value.GetPixels( size ) )
			);
			attributes.SetCombo( "D_BORDER_IMAGE", (byte)(style.BorderImageRepeat == BorderImageRepeat.Stretch ? 2 : 1) );
			attributes.Set( "HasBorderImageFill", (byte)(style.BorderImageFill == BorderImageFill.Filled ? 1 : 0) );

			attributes.Set( "BorderImageTint", style.BorderImageTint.Value.WithAlphaMultiplied( opacity ) );
		}
		else
		{
			attributes.SetCombo( "D_BORDER_IMAGE", 0 );
		}

		var texture = style.BackgroundImage;
		var backgroundRepeat = style.BackgroundRepeat ?? BackgroundRepeat.Repeat;
		if ( texture is not null && texture != Texture.Invalid )
		{
			var imageRectInput = new ImageRect.Input
			{
				ScaleToScreen = panel.ScaleToScreen,
				Image = texture,
				PanelRect = rect,
				DefaultSize = Length.Auto,
				ImagePositionX = style.BackgroundPositionX,
				ImagePositionY = style.BackgroundPositionY,
				ImageSizeX = style.BackgroundSizeX,
				ImageSizeY = style.BackgroundSizeY,
			};

			var imageCalc = ImageRect.Calculate( imageRectInput );

			attributes.Set( "Texture", texture );
			attributes.Set( "BgPos", imageCalc.Rect );
			attributes.Set( "BgAngle", style.BackgroundAngle.Value.GetPixels( 1.0f ) );
			attributes.Set( "BgRepeat", (int)backgroundRepeat );

			attributes.SetCombo( "D_BACKGROUND_IMAGE", 1 );

			attributes.Set( "BgTint", style.BackgroundTint.Value.WithAlphaMultiplied( opacity ) );
		}
		else
		{
			attributes.SetCombo( "D_BACKGROUND_IMAGE", 0 );
		}

		var filter = (style?.ImageRendering ?? ImageRendering.Anisotropic) switch
		{
			ImageRendering.Point => FilterMode.Point,
			ImageRendering.Bilinear => FilterMode.Bilinear,
			ImageRendering.Trilinear => FilterMode.Trilinear,
			_ => FilterMode.Anisotropic
		};

		var sampler = backgroundRepeat switch
		{
			BackgroundRepeat.RepeatX => new SamplerState { AddressModeV = TextureAddressMode.Clamp, Filter = filter },
			BackgroundRepeat.RepeatY => new SamplerState { AddressModeU = TextureAddressMode.Clamp, Filter = filter },
			BackgroundRepeat.Clamp => new SamplerState
			{
				AddressModeU = TextureAddressMode.Clamp,
				AddressModeV = TextureAddressMode.Clamp,
				Filter = filter
			},
			_ => new SamplerState { Filter = filter }
		};

		attributes.Set( "SamplerIndex", SamplerState.GetBindlessIndex( sampler ) );
		attributes.Set( "ClampSamplerIndex", SamplerState.GetBindlessIndex( new SamplerState
		{
			AddressModeU = TextureAddressMode.Clamp,
			AddressModeV = TextureAddressMode.Clamp,
			Filter = filter
		} ) );

		attributes.SetCombo( "D_BLENDMODE", panel.BackgroundBlendMode );
	}

	private void BuildCommandList_Background( Panel panel, ref RenderState state )
	{
		ThreadSafe.AssertIsMainThread();

		var attributes = panel.CommandList.Attributes;

		attributes.Set( "HasInverseScissor", 0 );
		panel.CommandList.InsertList( panel.ClipCommandList );

		UpdateRenderAttributes( attributes, panel );

		if ( panel.HasBackground )
		{
			// Texture has just loaded, rect needs to be recalculated
			{
				var texture = panel.ComputedStyle.BackgroundImage;
				if ( texture is not null && texture.IsDirty )
				{
					var imageCalc = ImageRect.Calculate( new ImageRect.Input
					{
						ScaleToScreen = panel.ScaleToScreen,
						Image = texture,
						PanelRect = panel.Box.Rect,
						DefaultSize = Length.Auto,
						ImagePositionX = panel.ComputedStyle.BackgroundPositionX,
						ImagePositionY = panel.ComputedStyle.BackgroundPositionY,
						ImageSizeX = panel.ComputedStyle.BackgroundSizeX,
						ImageSizeY = panel.ComputedStyle.BackgroundSizeY,
					} );

					attributes.Set( "BgPos", imageCalc.Rect );

					texture.IsDirty = false;
				}
			}

			var color = panel.ComputedStyle.BackgroundColor.Value;
			var opacity = panel.Opacity * state.RenderOpacity;
			color.a *= opacity;

			// Parameters influenced by opacity
			{
				attributes.Set( "BorderColorL", panel.ComputedStyle.BorderLeftColor.Value.WithAlphaMultiplied( opacity ) );
				attributes.Set( "BorderColorT", panel.ComputedStyle.BorderTopColor.Value.WithAlphaMultiplied( opacity ) );
				attributes.Set( "BorderColorR", panel.ComputedStyle.BorderRightColor.Value.WithAlphaMultiplied( opacity ) );
				attributes.Set( "BorderColorB", panel.ComputedStyle.BorderBottomColor.Value.WithAlphaMultiplied( opacity ) );
				attributes.Set( "BorderImageTint", panel.ComputedStyle.BorderImageTint.Value.WithAlphaMultiplied( opacity ) );
				attributes.Set( "BgTint", panel.ComputedStyle.BackgroundTint.Value.WithAlphaMultiplied( opacity ) );
			}

			var rect = panel.Box.Rect;

			{
				attributes.Set( "BoxPosition", new Vector2( rect.Left, rect.Top ) );
				attributes.Set( "BoxSize", new Vector2( rect.Width, rect.Height ) );
			}

			if ( panel.BackgroundBlendMode == BlendMode.Normal || panel.ComputedStyle.BackgroundImage == null )
			{
				attributes.SetCombo( "D_BLENDMODE", OverrideBlendMode );
				attributes.Set( "Texture", panel.ComputedStyle.BackgroundImage );
				panel.CommandList.DrawQuad( rect, Material.UI.Box, color );
			}
			else
			{
				// Draw background color
				attributes.SetCombo( "D_BLENDMODE", OverrideBlendMode );
				attributes.Set( "Texture", Texture.Invalid );
				panel.CommandList.DrawQuad( rect, Material.UI.Box, color );

				// Draw background image with specified background-blend-mode
				attributes.SetCombo( "D_BLENDMODE", panel.BackgroundBlendMode );
				attributes.Set( "Texture", panel.ComputedStyle.BackgroundImage );
				panel.CommandList.DrawQuad( rect, Material.UI.Box, color );
			}
		}

		panel.BuildCommandList( panel.CommandList );
	}
}
