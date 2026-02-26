using Sandbox.Rendering;

namespace Sandbox.UI;

internal partial class PanelRenderer
{
	private void SetColor( CommandList.AttributeAccess attributes, string v, Color color, float opacity )
	{
		if ( opacity < 1 )
		{
			color.a *= opacity;
		}

		attributes.Set( v, color );
	}

	void SetBorderRadius( CommandList.AttributeAccess attributes, Styles style, float size )
	{
		var borderRadius = new Vector4(
			style.BorderBottomRightRadius.Value.GetPixels( size ),
			style.BorderTopRightRadius.Value.GetPixels( size ),
			style.BorderBottomLeftRadius.Value.GetPixels( size ),
			style.BorderTopLeftRadius.Value.GetPixels( size )
		);

		attributes.Set( "BorderRadius", borderRadius );
	}

	public void BuildCommandList_BackgroundTexture( Panel panel, Texture texture, in RenderState state, Length defaultSize )
	{
		ThreadSafe.AssertIsMainThread();

		var style = panel.ComputedStyle;
		if ( style == null ) return;

		if ( texture == Texture.Invalid )
			texture = null;

		var attributes = panel.CommandList.Attributes;

		attributes.Set( "HasInverseScissor", 0 );
		panel.CommandList.InsertList( panel.TransformCommandList );
		panel.CommandList.InsertList( panel.ClipCommandList );

		var rect = panel.Box.Rect;
		var opacity = panel.Opacity * state.RenderOpacity;

		var color = style.BackgroundColor.Value;
		color.a *= opacity;

		var size = (rect.Width + rect.Height) * 0.5f;

		var borderSize = new Vector4(
			style.BorderLeftWidth.Value.GetPixels( size ),
			style.BorderTopWidth.Value.GetPixels( size ),
			style.BorderRightWidth.Value.GetPixels( size ),
			style.BorderBottomWidth.Value.GetPixels( size )
		);

		attributes.Set( "BoxPosition", new Vector2( rect.Left, rect.Top ) );
		attributes.Set( "BoxSize", new Vector2( rect.Width, rect.Height ) );

		SetBorderRadius( attributes, style, size );

		if ( borderSize.x == 0 && borderSize.y == 0 && borderSize.z == 0 && borderSize.w == 0 )
		{
			attributes.Set( "HasBorder", 0 );
		}
		else
		{
			attributes.Set( "HasBorder", 1 );
			attributes.Set( "BorderSize", borderSize );

			SetColor( attributes, "BorderColorL", style.BorderLeftColor.Value, opacity );
			SetColor( attributes, "BorderColorT", style.BorderTopColor.Value, opacity );
			SetColor( attributes, "BorderColorR", style.BorderRightColor.Value, opacity );
			SetColor( attributes, "BorderColorB", style.BorderBottomColor.Value, opacity );
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

			SetColor( attributes, "BorderImageTint", style.BorderImageTint.Value, opacity );
		}
		else
		{
			attributes.SetCombo( "D_BORDER_IMAGE", 0 );
		}

		var backgroundRepeat = style.BackgroundRepeat ?? BackgroundRepeat.Repeat;

		if ( texture != null )
		{
			var imageRectInput = new ImageRect.Input
			{
				ScaleToScreen = panel.ScaleToScreen,
				Image = texture,
				PanelRect = rect,
				DefaultSize = defaultSize,
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

			SetColor( attributes, "BgTint", style.BackgroundTint.Value, opacity );
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

		attributes.SetCombo( "D_BLENDMODE", OverrideBlendMode );

		panel.CommandList.DrawQuad( rect, Material.UI.Box, color );
	}

	private void BuildCommandList( Panel panel, ref RenderState state )
	{
		ThreadSafe.AssertIsMainThread();

		panel.CommandList.Reset();

		// Insert transform (TransformMat attribute)
		panel.CommandList.InsertList( panel.TransformCommandList );

		// Set up scissor from current renderer state
		panel.ClipCommandList.Reset();
		SetScissorAttributes( panel.ClipCommandList, ScissorGPU );

		// Insert clip (scissor attributes)
		panel.CommandList.InsertList( panel.ClipCommandList );

		// Push layer so everything renders into the layer texture for filters/masks
		panel.PushLayer( this );

		// Draw backdrops (e.g. blurs)
		BuildCommandList_Backdrop( panel, ref state );

		// Draw box shadows (outset, underlay)
		BuildCommandList_BoxShadows( panel, ref state, inset: false );

		// Draw background (e.g. color, image)
		BuildCommandList_Background( panel, ref state );

		// Draw box shadows (inset, overlay)
		BuildCommandList_BoxShadows( panel, ref state, inset: true );

		panel.IsRenderDirty = false;
	}
}
