using Sandbox.UI;
using System.Runtime.CompilerServices;

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
		if ( !context.State.HasArea || context.State.Opacity == 0 ) return;

		var blendMode = ResolveBlendMode( desc, context.State.OverrideBlendMode );
		var transform = DrawingTransform( context );
		var clipIndex = context.State.ClipIndex;
		context.Batcher.Add( desc, context.State.Opacity, blendMode, transform, clipIndex );
	}

	/// <summary>
	/// Submits a solid shape without creating a CSS box descriptor. Text masks use the
	/// general path so that texture lifetime and presentation tracking stay centralized.
	/// </summary>
	static bool TryAddSolidShape( Context context, in Fill fill, Rect bounds, int shapeIndex, bool clipFill = true )
	{
		ref var state = ref context.State;
		if ( !fill.TryGetSolidColor( out var color ) || (clipFill && state.FillMask is not null) )
			return false;

		if ( !state.HasArea || state.Opacity == 0 ) return true;

		color = color.WithAlphaMultiplied( context.InheritedOpacity ).WithAlphaMultiplied( state.Opacity );
		var clip = clipFill && state.FillInsets != Vector4.Zero ? BackgroundClip.ContentBox : BackgroundClip.BorderBox;
		context.Batcher.AddSolidShape( bounds, color, shapeIndex, clip, state.FillInsets,
			DrawingTransform( context ), state.ClipIndex, state.OverrideBlendMode );
		return true;
	}

	static void AddShape( Context context, in Fill fill, Rect bounds, Rect paintBounds, int shapeIndex, bool clipFill = true )
	{
		if ( TryAddSolidShape( context, fill, bounds, shapeIndex, clipFill ) ) return;
		AddPaintedShape( context, fill, bounds, paintBounds, shapeIndex, clipFill );
	}

	/// <summary>
	/// Keeps descriptor storage outside the solid draw's stack frame. Paint coordinates
	/// remain relative to the full undashed shape when coverage bounds are smaller.
	/// </summary>
	[MethodImpl( MethodImplOptions.NoInlining )]
	static void AddPaintedShape( Context context, in Fill fill, Rect bounds, Rect paintBounds, int shapeIndex, bool clipFill )
	{
		fill.CreateDescriptor( paintBounds, context, out var descriptor, clipFill );
		descriptor.Rect = bounds;
		descriptor.BackgroundRect.x += paintBounds.Left - bounds.Left;
		descriptor.BackgroundRect.y += paintBounds.Top - bounds.Top;
		descriptor.ShapeIndex = shapeIndex;
		Add( context, descriptor );
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
