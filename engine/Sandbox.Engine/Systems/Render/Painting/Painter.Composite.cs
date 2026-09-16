using Sandbox.Rendering;
using Sandbox.UI;

namespace Sandbox;

public readonly ref partial struct Painter
{
	internal void Composite( RenderTargetHandle source, Rect bounds, Filter filter, Mask? mask, MaskScope maskScope,
		ReadOnlySpan<UI.Shadow> shadows, float borderWidth, Color borderColor )
	{
		var commands = NativeCommands();
		var attributes = commands.Attributes;
		attributes.Set( "Texture", source.ColorTexture );
		attributes.Set( "BoxPosition", bounds.Position );
		attributes.Set( "BoxSize", bounds.Size );

		foreach ( var shadow in shadows )
		{
			var offset = new Vector2( shadow.OffsetX, shadow.OffsetY );
			var padding = MathF.Max( MathF.Abs( offset.x ), MathF.Abs( offset.y ) ) + MathF.Ceiling( shadow.Blur * 3 ) + 1;
			var rect = bounds.Grow( padding );
			ResetCompositeAttributes( commands );
			attributes.Set( "FilterDropShadowScale", bounds.Size / rect.Size );
			attributes.Set( "FilterDropShadowOffset", offset );
			attributes.Set( "FilterDropShadowBlur", shadow.Blur );
			attributes.Set( "FilterDropShadowColor", shadow.Color );
			commands.DrawQuad( rect, Material.UI.DropShadow, Color.White );
			Output.CountDraw();
		}

		if ( borderWidth > 0 )
		{
			var rect = bounds.Grow( borderWidth );
			ResetCompositeAttributes( commands );
			attributes.Set( "FilterBorderWrapColorScale", bounds.Size / rect.Size );
			attributes.Set( "FilterBorderWrapColor", borderColor );
			attributes.Set( "FilterBorderWrapWidth", borderWidth );
			commands.DrawQuad( rect, Material.UI.BorderWrap, Color.White );
			Output.CountDraw();
		}

		ResetCompositeAttributes( commands );
		attributes.Set( "PainterSourcePremultiplied", false );
		attributes.Set( "PainterScissorIndex", -1 );
		SetFilterAttributes( attributes, bounds, filter, mask, maskScope );

		attributes.SetCombo( "D_BLENDMODE", InheritedBlendMode );
		commands.DrawQuad( bounds.Grow( MathF.Ceiling( filter.Blur * 3 ) ).Ceiling(), Material.UI.Filter, Color.White );
		Output.CountDraw();
	}

	void CompositeLayer( RenderTargetHandle source, Rect bounds, Filter filter, Mask? mask, float opacity )
	{
		var context = ActiveContext;
		var output = context.Batcher;
		output.Flush();
		var commands = output.CommandList;
		var attributes = commands.BeginDrawAttributes();
		var target = output.Destination;
		var transform = context.State.Transform * context.BaseTransform * target.Transform;
		var clip = output.GetOrAddDrawClip( context.State.ClipIndex, target.Transform, output.GetOrAddScissor( target.Scissor ) );
		output.BindScissor( attributes, clip );
		attributes.Set( "HasScissor", 0 );
		attributes.Set( "Texture", source.ColorTexture );
		attributes.Set( "PainterSourcePremultiplied", true );
		attributes.Set( "LayerMat", target.LayerMatrix );
		attributes.Set( "TransformMat", transform );
		attributes.SetCombo( "D_WORLDPANEL", target.WorldPanelCombo );
		// Native quads apply the scene object's transform in ui/vertex.hlsl.
		if ( target.WorldMatrix.HasValue ) attributes.Set( "WorldMat", ScenePanelObject.BuildPanelToObjectMatrix() );
		if ( target.GammaOutput.HasValue ) attributes.Set( "UIGammaOutput", target.GammaOutput.Value );
		attributes.Set( "BoxPosition", bounds.Position );
		attributes.Set( "BoxSize", bounds.Size );
		filter = filter with { Tint = filter.Tint.WithAlphaMultiplied( context.State.Opacity * context.InheritedOpacity * opacity ) };
		SetFilterAttributes( attributes, bounds, filter, mask, MaskScope.Default );
		attributes.SetCombo( "D_BLENDMODE", context.State.OverrideBlendMode );
		commands.DrawQuad( bounds.Grow( MathF.Ceiling( filter.Blur * 3 ) ), Material.UI.Filter, Color.White, attributes );
		output.CountDraw();
	}

	void SetFilterAttributes( CommandList.AttributeAccess attributes, Rect bounds, Filter filter, Mask? mask, MaskScope maskScope )
	{
		attributes.Set( "FilterBlur", filter.Blur );
		attributes.Set( "FilterSaturate", filter.Saturation );
		attributes.Set( "FilterSepia", filter.Sepia );
		attributes.Set( "FilterBrightness", filter.Brightness );
		attributes.Set( "FilterContrast", filter.Contrast );
		attributes.Set( "FilterInvert", filter.Invert );
		attributes.Set( "FilterHueRotate", filter.HueRotation );
		attributes.Set( "FilterTint", filter.Tint );
		attributes.SetCombo( "D_MASK_IMAGE", mask.HasValue ? 1 : 0 );
		if ( mask is Mask m )
		{
			Output.RetainTexture( m.Texture, true );
			attributes.Set( "MaskTexture", m.Texture );
			attributes.Set( "MaskPos", new Vector4( m.Rect.Left - bounds.Left, m.Rect.Top - bounds.Top, m.Rect.Width, m.Rect.Height ) );
			attributes.Set( "MaskMode", (int)m.Mode );
			attributes.Set( "MaskScope", (int)maskScope );
			attributes.Set( "MaskAngle", m.Rotation.DegreeToRadian() );
			var sampler = m.Repeat switch
			{
				BackgroundRepeat.RepeatX => new SamplerState { AddressModeV = TextureAddressMode.Clamp, Filter = m.Sampling },
				BackgroundRepeat.RepeatY => new SamplerState { AddressModeU = TextureAddressMode.Clamp, Filter = m.Sampling },
				BackgroundRepeat.NoRepeat => new SamplerState { AddressModeU = TextureAddressMode.Border, AddressModeV = TextureAddressMode.Border, Filter = m.Sampling },
				BackgroundRepeat.Clamp => new SamplerState { AddressModeU = TextureAddressMode.Clamp, AddressModeV = TextureAddressMode.Clamp, Filter = m.Sampling },
				_ => new SamplerState { Filter = m.Sampling }
			};
			attributes.Set( "SamplerIndex", SamplerState.GetBindlessIndex( sampler ) );
			attributes.Set( "BorderSamplerIndex", SamplerState.GetBindlessIndex( new SamplerState
			{
				AddressModeU = TextureAddressMode.Border,
				AddressModeV = TextureAddressMode.Border,
				Filter = m.Sampling
			} ) );
		}
	}

	static void ResetCompositeAttributes( CommandList commands )
	{
		var attributes = commands.Attributes;
		attributes.Set( "FilterDropShadowScale", 0 );
		attributes.Set( "FilterDropShadowOffset", 0 );
		attributes.Set( "FilterDropShadowBlur", 0 );
		attributes.Set( "FilterDropShadowColor", 0 );
		attributes.Set( "FilterBorderWrapColor", 0 );
		attributes.Set( "FilterBorderWrapWidth", 0 );
	}
}
