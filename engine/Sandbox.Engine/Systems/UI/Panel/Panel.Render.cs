using Sandbox.Rendering;

namespace Sandbox.UI;

public partial class Panel
{
	bool backgroundRenderDirty = true;
	RenderAttributes bgAttribs;
	BlendMode bgBlendMode;

	internal virtual void DrawBackground( PanelRenderer renderer, ref RenderState state )
	{
		if ( HasBackdropFilter )
		{
			renderer.DrawBackdropFilters( this, state );
		}

		if ( backgroundRenderDirty )
		{
			UpdateBackgroundData();
			backgroundRenderDirty = false;
		}

		if ( HasBackground )
		{
			// Texture has just loaded, rect needs to be recalculated
			{
				var texture = ComputedStyle.BackgroundImage;
				if ( texture is not null && texture.IsDirty )
				{
					var imageCalc = ImageRect.Calculate( new ImageRect.Input
					{
						ScaleToScreen = ScaleToScreen,
						Image = texture,
						PanelRect = Box.Rect,
						DefaultSize = Length.Auto,
						ImagePositionX = ComputedStyle.BackgroundPositionX,
						ImagePositionY = ComputedStyle.BackgroundPositionY,
						ImageSizeX = ComputedStyle.BackgroundSizeX,
						ImageSizeY = ComputedStyle.BackgroundSizeY,
					} );

					bgAttribs.Set( "BgPos", imageCalc.Rect );

					texture.IsDirty = false;
				}
			}

			var color = ComputedStyle.BackgroundColor.Value;
			var opacity = Opacity * state.RenderOpacity;
			color.a *= opacity;

			// parameters inflienced by opacity !
			{
				bgAttribs.Set( "BorderColorL", ComputedStyle.BorderLeftColor.Value.WithAlphaMultiplied( opacity ) );
				bgAttribs.Set( "BorderColorT", ComputedStyle.BorderTopColor.Value.WithAlphaMultiplied( opacity ) );
				bgAttribs.Set( "BorderColorR", ComputedStyle.BorderRightColor.Value.WithAlphaMultiplied( opacity ) );
				bgAttribs.Set( "BorderColorB", ComputedStyle.BorderBottomColor.Value.WithAlphaMultiplied( opacity ) );
				bgAttribs.Set( "BorderImageTint", ComputedStyle.BorderImageTint.Value.WithAlphaMultiplied( opacity ) );
				bgAttribs.Set( "BgTint", ComputedStyle.BackgroundTint.Value.WithAlphaMultiplied( opacity ) );
			}

			var rect = Box.Rect;

			{
				bgAttribs.Set( "BoxPosition", new Vector2( rect.Left, rect.Top ) );
				bgAttribs.Set( "BoxSize", new Vector2( rect.Width, rect.Height ) );
			}

			if ( bgBlendMode == BlendMode.Normal || ComputedStyle.BackgroundImage == null )
			{
				BlendMode bm = bgBlendMode;

				if ( bm == BlendMode.Normal && (ComputedStyle.BackgroundImage?.Flags.Contains( TextureFlags.PremultipliedAlpha ) ?? false) )
					bm = BlendMode.PremultipliedAlpha;

				bgAttribs.SetComboEnum( "D_BLENDMODE", bm );
				bgAttribs.Set( "Texture", ComputedStyle.BackgroundImage );
				Graphics.DrawQuad( rect, Material.UI.Box, color, bgAttribs );
			}
			else
			{
				// Draw background color
				bgAttribs.SetComboEnum( "D_BLENDMODE", renderer.OverrideBlendMode );
				bgAttribs.Set( "Texture", Texture.Invalid );
				Graphics.DrawQuad( rect, Material.UI.Box, color, bgAttribs );

				// Draw background image with specified background-blend-mode
				bgAttribs.SetComboEnum( "D_BLENDMODE", bgBlendMode );
				bgAttribs.Set( "Texture", ComputedStyle.BackgroundImage );
				Graphics.DrawQuad( rect, Material.UI.Box, color, bgAttribs );
			}


		}

		DrawBackground( ref state );
	}

	void UpdateBackgroundData()
	{
		bgBlendMode = ParseBlendMode( ComputedStyle?.BackgroundBlendMode );

		var style = ComputedStyle;
		if ( style is null ) return;

		bgAttribs ??= new RenderAttributes();

		var rect = Box.Rect;
		var opacity = Opacity * 1;

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

		bgAttribs.Set( "BorderRadius", borderRadius );

		if ( borderSize.x == 0 && borderSize.y == 0 && borderSize.z == 0 && borderSize.w == 0 )
		{
			bgAttribs.Set( "HasBorder", 0 );
		}
		else
		{
			bgAttribs.Set( "HasBorder", 1 );
			bgAttribs.Set( "BorderSize", borderSize );

			bgAttribs.Set( "BorderColorL", style.BorderLeftColor.Value.WithAlphaMultiplied( opacity ) );
			bgAttribs.Set( "BorderColorT", style.BorderTopColor.Value.WithAlphaMultiplied( opacity ) );
			bgAttribs.Set( "BorderColorR", style.BorderRightColor.Value.WithAlphaMultiplied( opacity ) );
			bgAttribs.Set( "BorderColorB", style.BorderBottomColor.Value.WithAlphaMultiplied( opacity ) );
		}

		// We have a border image
		if ( style.BorderImageSource != null )
		{
			bgAttribs.Set( "BorderImageTexture", style.BorderImageSource );
			bgAttribs.Set( "BorderImageSlice", new Vector4(
				style.BorderImageWidthLeft.Value.GetPixels( size ),
				style.BorderImageWidthTop.Value.GetPixels( size ),
				style.BorderImageWidthRight.Value.GetPixels( size ),
				style.BorderImageWidthBottom.Value.GetPixels( size ) )
			);
			bgAttribs.SetCombo( "D_BORDER_IMAGE", (byte)(style.BorderImageRepeat == BorderImageRepeat.Stretch ? 2 : 1) );
			bgAttribs.Set( "HasBorderImageFill", (byte)(style.BorderImageFill == BorderImageFill.Filled ? 1 : 0) );

			bgAttribs.Set( "BorderImageTint", style.BorderImageTint.Value.WithAlphaMultiplied( opacity ) );
		}
		else
		{
			bgAttribs.SetCombo( "D_BORDER_IMAGE", 0 );
		}

		var texture = style.BackgroundImage;
		var backgroundRepeat = style.BackgroundRepeat ?? BackgroundRepeat.Repeat;
		if ( texture is not null && texture != Texture.Invalid )
		{
			var imageRectInput = new ImageRect.Input
			{
				ScaleToScreen = ScaleToScreen,
				Image = texture,
				PanelRect = rect,
				DefaultSize = Length.Auto,
				ImagePositionX = style.BackgroundPositionX,
				ImagePositionY = style.BackgroundPositionY,
				ImageSizeX = style.BackgroundSizeX,
				ImageSizeY = style.BackgroundSizeY,
			};

			var imageCalc = ImageRect.Calculate( imageRectInput );

			bgAttribs.Set( "Texture", texture );
			bgAttribs.Set( "BgPos", imageCalc.Rect );
			bgAttribs.Set( "BgAngle", style.BackgroundAngle.Value.GetPixels( 1.0f ) );
			bgAttribs.Set( "BgRepeat", (int)backgroundRepeat );

			bgAttribs.SetCombo( "D_BACKGROUND_IMAGE", 1 );

			bgAttribs.Set( "BgTint", style.BackgroundTint.Value.WithAlphaMultiplied( opacity ) );
		}
		else
		{
			bgAttribs.SetCombo( "D_BACKGROUND_IMAGE", 0 );
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

		bgAttribs.Set( "SamplerIndex", SamplerState.GetBindlessIndex( sampler ) );
		bgAttribs.Set( "ClampSamplerIndex", SamplerState.GetBindlessIndex( new SamplerState
		{
			AddressModeU = TextureAddressMode.Clamp,
			AddressModeV = TextureAddressMode.Clamp,
			Filter = filter
		} ) );

		bgAttribs.SetComboEnum( "D_BLENDMODE", bgBlendMode );
	}

	private BlendMode ParseBlendMode( string blendModeStr )
	{
		var blendMode = blendModeStr switch
		{
			"lighten" => BlendMode.Lighten,
			"multiply" => BlendMode.Multiply,
			_ => BlendMode.Normal,
		};

		return blendMode;
	}

	internal virtual void DrawContent( PanelRenderer renderer, ref RenderState state )
	{
		DrawContent( ref state );
	}

	/// <summary>
	/// Called when <see cref="HasContent"/> is set to <see langword="true"/> to custom draw the panels content.
	/// </summary>
	public virtual void DrawContent( ref RenderState state )
	{
		// nothing by default
	}

	/// <summary>
	/// Called to draw the panels background.
	/// </summary>
	public virtual void DrawBackground( ref RenderState state )
	{
		// nothing by default
	}

	internal void RenderChildren( PanelRenderer render, ref RenderState state )
	{
		using var _ = render.Clip( this );

		if ( _renderChildrenDirty )
		{
			_renderChildren.Sort( ( x, y ) => x.GetRenderOrderIndex() - y.GetRenderOrderIndex() );
			_renderChildrenDirty = false;
		}

		// Render Children
		{
			for ( int i = 0; i < _renderChildren.Count; i++ )
			{
				render.Render( _renderChildren[i], state );
			}
		}
	}
}
