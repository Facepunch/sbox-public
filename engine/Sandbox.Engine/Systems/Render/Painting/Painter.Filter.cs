using Sandbox.Rendering;
using Sandbox.UI;

namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Color and blur filters for an offscreen layer or backdrop. Use new Filter() for identity defaults.
	/// </summary>
	public readonly record struct Filter
	{
		/// <summary>
		/// Blur radius in render-target pixels. Must be finite and non-negative; zero disables blur.
		/// </summary>
		public float Blur { get; init; }

		/// <summary>
		/// Non-negative brightness multiplier. One preserves brightness; zero produces black.
		/// </summary>
		public float Brightness { get; init; } = 1;

		/// <summary>
		/// Non-negative contrast multiplier. One preserves contrast; zero produces uniform gray.
		/// </summary>
		public float Contrast { get; init; } = 1;

		/// <summary>
		/// Non-negative saturation multiplier. One preserves saturation; zero removes color.
		/// </summary>
		public float Saturation { get; init; } = 1;

		/// <summary>
		/// Sepia amount from zero (unchanged) to one (full sepia).
		/// </summary>
		public float Sepia { get; init; }

		/// <summary>
		/// Color inversion amount from zero (unchanged) to one (fully inverted).
		/// </summary>
		public float Invert { get; init; }

		/// <summary>
		/// Hue rotation in degrees. Must be finite; zero leaves the hue unchanged.
		/// </summary>
		public float HueRotation { get; init; }

		/// <summary>
		/// Color multiplier applied to the filtered result, including alpha. Defaults to white.
		/// </summary>
		public Color Tint { get; init; } = Color.White;

		/// <summary>
		/// Creates an identity filter with no blur or color adjustment.
		/// </summary>
		public Filter() { }

		internal void Validate()
		{
			if ( !float.IsFinite( Blur ) || Blur < 0 || !float.IsFinite( Brightness ) || Brightness < 0
				|| !float.IsFinite( Contrast ) || Contrast < 0 || !float.IsFinite( Saturation ) || Saturation < 0
				|| !float.IsFinite( Sepia ) || Sepia < 0 || Sepia > 1 || !float.IsFinite( Invert ) || Invert < 0 || Invert > 1 || !float.IsFinite( HueRotation ) )
				throw new ArgumentOutOfRangeException( nameof( Filter ) );
			if ( !float.IsFinite( Tint.r ) || !float.IsFinite( Tint.g ) || !float.IsFinite( Tint.b ) || !float.IsFinite( Tint.a ) )
				throw new ArgumentOutOfRangeException( nameof( Tint ), "Filter tint must be finite." );
		}
	}

	/// <summary>
	/// An image mask positioned in drawing coordinates. Applied to the completed layer.
	/// </summary>
	/// <param name="Texture">Mask image. Must be non-null when used by a layer.</param>
	/// <param name="Rect">Image position and size in drawing coordinates. Must have finite, positive dimensions.</param>
	/// <param name="Mode">Whether image alpha or luminance controls layer visibility. Defaults to alpha.</param>
	/// <param name="Repeat">Image repetition outside the mask rectangle. Defaults to no repetition.</param>
	/// <param name="Rotation">Rotation around the image center in degrees. Must be finite.</param>
	/// <param name="Sampling">Texture sampling filter. Defaults to bilinear sampling.</param>
	public readonly record struct Mask( Texture Texture, Rect Rect, MaskMode Mode = MaskMode.Alpha,
		BackgroundRepeat Repeat = BackgroundRepeat.NoRepeat, float Rotation = 0, FilterMode Sampling = FilterMode.Bilinear );

	/// <summary>
	/// Filter pixels already drawn behind a rectangle. Captures the current transform, clip, opacity and blend mode.
	/// </summary>
	public void FilterBackdrop( Rect rect, Filter filter, CornerRadii corners = default )
	{
		if ( !ValidBounds( rect ) ) throw new ArgumentOutOfRangeException( nameof( rect ) );
		filter.Validate();
		var context = ActiveContext;
		if ( !context.State.HasArea || context.State.Opacity == 0 ) return;
		context.Batcher.AddBackdrop( new BackdropData( rect, filter, corners.Resolve( rect ), context.State.Opacity * context.InheritedOpacity ),
			context.State.Transform * context.BaseTransform, context.State.ClipIndex, context.State.OverrideBlendMode );
	}

	internal readonly record struct BackdropData( Rect Rect, Filter Filter, BorderRadii Radii, float Opacity );
}
