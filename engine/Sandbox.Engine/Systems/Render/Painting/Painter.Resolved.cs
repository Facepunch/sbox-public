using Sandbox.UI;

namespace Sandbox;

public readonly ref partial struct Painter
{
	internal LegacyPaint.Binding BindLegacy() => new( GetActiveContext() );

	static Matrix DrawingTransform( Context context ) => context.State.Transform == Matrix.Identity ? context.BaseTransform : context.State.Transform * context.BaseTransform;

	static BlendMode ResolveBlendMode( in BoxDescriptor desc, BlendMode blendMode )
	{
		if ( blendMode == BlendMode.Normal && desc.HasImage
			&& desc.BackgroundImage.Flags.HasFlag( TextureFlags.PremultipliedAlpha ) )
			return BlendMode.PremultipliedAlpha;

		return blendMode;
	}

	static void Add( Context context, in BoxDescriptor desc )
	{
		var blendMode = ResolveBlendMode( desc, context.State.OverrideBlendMode );
		var transform = DrawingTransform( context );
		var clipIndex = context.State.ClipIndex;
		if ( !context.State.HasArea || context.State.Opacity == 0 ) return;
		var opacity = context.State.Opacity;
		if ( opacity == 1 && desc.OverrideBlendMode == blendMode )
		{
			context.Batcher.Add( desc, transform, clipIndex );
			return;
		}
		context.Batcher.Add( desc with
		{
			OverrideBlendMode = blendMode,
			Color = desc.Color.WithAlphaMultiplied( opacity ),
			BackgroundTint = desc.BackgroundTint.WithAlphaMultiplied( opacity ),
			Stroke = desc.Stroke.WithAlphaMultiplied( opacity ),
			BorderImage = desc.BorderImage.WithAlphaMultiplied( opacity ),
		}, transform, clipIndex );
	}

	static void Add( Context context, in ShadowDescriptor desc )
	{
		if ( context.State.HasArea && context.State.Opacity > 0 )
			context.Batcher.Add( desc with { Color = desc.Color.WithAlphaMultiplied( context.State.Opacity ), OverrideBlendMode = context.State.OverrideBlendMode }, DrawingTransform( context ), context.State.ClipIndex );
	}

	static void Add( Context context, in OutlineDescriptor desc )
	{
		if ( context.State.HasArea && context.State.Opacity > 0 )
			context.Batcher.Add( desc with { Color = desc.Color.WithAlphaMultiplied( context.State.Opacity ), OverrideBlendMode = context.State.OverrideBlendMode }, DrawingTransform( context ), context.State.ClipIndex );
	}
}
