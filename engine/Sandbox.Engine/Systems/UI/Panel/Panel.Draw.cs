using Sandbox.Rendering;

namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// To be used inside <see cref="OnDraw"/> to add custom shapes, textures and text to a panel.
	/// These draw calls will be batched together with the panel's CSS-styled content for efficient rendering.
	/// <example>
	/// <code>
	/// public override void OnDraw()
	/// {
	///     Draw.Rect( new Rect( 0, 0, 100, 100 ), Color.Red, cornerRadius: 8 );
	///     Draw.Text( "Hello", new Rect( 10, 10, 80, 20 ), 14, Color.White );
	/// }
	/// </code>
	/// </example>
	/// </summary>
	public static class Draw
	{
		/// <summary>
		/// Draws a filled rectangle.
		/// </summary>
		/// <param name="rect">The rectangle to draw, in panel-local coordinates.</param>
		/// <param name="color">Fill color.</param>
		/// <param name="cornerRadius">Uniform corner radius for rounded rectangles. Use the <see cref="Rect(Rect, Color, Vector4)"/> overload for per-corner control.</param>
		public static void Rect( Rect rect, Color color, float cornerRadius = 0 )
		{
			UIDrawBuffer.Current.AddBox( new BoxDrawDescriptor( rect, color )
			{
				BorderRadius = new Vector4( cornerRadius ),
			} );
		}

		/// <summary>
		/// Draws a filled rectangle with per-corner radius control.
		/// </summary>
		/// <param name="rect">The rectangle to draw, in panel-local coordinates.</param>
		/// <param name="color">Fill color.</param>
		/// <param name="cornerRadius">Corner radii as (bottom-right, top-right, bottom-left, top-left).</param>
		public static void Rect( Rect rect, Color color, Vector4 cornerRadius )
		{
			UIDrawBuffer.Current.AddBox( new BoxDrawDescriptor( rect, color )
			{
				BorderRadius = cornerRadius,
			} );
		}

		/// <summary>
		/// Draws a filled circle.
		/// </summary>
		/// <param name="center">Center position in panel-local coordinates.</param>
		/// <param name="radius">Circle radius in pixels.</param>
		/// <param name="color">Fill color.</param>
		public static void Circle( Vector2 center, float radius, Color color )
		{
			var size = new Vector2( radius * 2f );
			var rect = new Rect( center - size * 0.5f, size );
			Rect( rect, color, radius * 2f );
		}

		/// <summary>
		/// Draws a texture within the given rectangle.
		/// </summary>
		/// <param name="texture">The texture to draw.</param>
		/// <param name="rect">Destination rectangle in panel-local coordinates.</param>
		/// <param name="tint">Optional color tint applied to the texture. Defaults to <see cref="Color.White"/> (no tint).</param>
		public static void Texture( Texture texture, Rect rect, Color? tint = null )
		{
			UIDrawBuffer.Current.AddBox( new BoxDrawDescriptor( rect, tint ?? Color.White )
			{
				BackgroundImage = texture,
				BackgroundTint = tint ?? Color.White,
				BackgroundRepeat = BackgroundRepeat.Clamp,
			} );
		}

		/// <summary>
		/// Draws a text string within the given rectangle.
		/// </summary>
		/// <param name="text">The text to render.</param>
		/// <param name="rect">Bounding rectangle for text layout, in panel-local coordinates.</param>
		/// <param name="size">Font size in pixels.</param>
		/// <param name="color">Text color.</param>
		/// <param name="flags">Text alignment and layout flags. Defaults to <see cref="TextFlag.LeftTop"/>.</param>
		public static void Text( string text, Rect rect, float size, Color color, TextFlag flags = TextFlag.LeftTop )
		{
			var scope = new TextRendering.Scope( text, color, size );
			var texture = TextRendering.GetOrCreateTexture( scope, rect.Size, flags );
			if ( texture is null ) return;

			var textRect = rect.Align( texture.Size, flags );

			UIDrawBuffer.Current.AddBox( new BoxDrawDescriptor( textRect, Color.White )
			{
				BackgroundImage = texture,
				BackgroundRect = new Vector4( 0, 0, textRect.Width, textRect.Height ),
				BackgroundTint = color,
				OverrideBlendMode = BlendMode.PremultipliedAlpha,
			} );
		}

		/// <summary>
		/// Draws a box shadow (drop shadow or inset shadow).
		/// </summary>
		/// <param name="rect">The rectangle to cast the shadow from, in panel-local coordinates.</param>
		/// <param name="color">Shadow color.</param>
		/// <param name="blur">Blur radius in pixels. Higher values produce softer shadows.</param>
		/// <param name="spread">Spread distance in pixels. Positive values expand the shadow, negative values shrink it.</param>
		/// <param name="offset">Shadow offset from the rectangle position.</param>
		/// <param name="cornerRadius">Corner radius to match rounded rectangles.</param>
		/// <param name="inset">If true, draws an inner shadow instead of a drop shadow.</param>
		public static void Shadow( Rect rect, Color color, float blur = 0, float spread = 0, Vector2 offset = default, float cornerRadius = 0, bool inset = false )
		{
			UIDrawBuffer.Current.AddShadow( new ShadowDrawDescriptor( rect, color )
			{
				BorderRadius = new Vector4( cornerRadius ),
				Blur = blur,
				Spread = spread,
				Offset = offset,
				Inset = inset,
			} );
		}

		/// <summary>
		/// Draws an outline (stroke) around a rectangle.
		/// </summary>
		/// <param name="rect">The rectangle to outline, in panel-local coordinates.</param>
		/// <param name="color">Outline color.</param>
		/// <param name="width">Outline thickness in pixels.</param>
		/// <param name="cornerRadius">Corner radius to match rounded rectangles.</param>
		/// <param name="offset">Outline offset. Positive values push the outline outward, negative values pull it inward.</param>
		public static void Outline( Rect rect, Color color, float width, float cornerRadius = 0, float offset = 0 )
		{
			UIDrawBuffer.Current.AddOutline( new OutlineDrawDescriptor( rect, color, width )
			{
				BorderRadius = new Vector4( cornerRadius ),
				Offset = offset,
			} );
		}
	}

	/// <summary>
	/// Draws a texture using this panel's CSS box styling (border radius, border image, background position/size,
	/// tint, blend mode, filter mode, etc.) and adds the resulting descriptor to <see cref="CachedDescriptors"/>.
	/// <para>
	/// This is intended for controls like <see cref="Image"/>, <see cref="ScenePanel"/>, and <see cref="SvgPanel"/>
	/// that render a texture as their primary content while respecting the panel's CSS properties.
	/// For simple texture drawing without CSS styling, use <see cref="Draw.Texture"/> instead.
	/// </para>
	/// </summary>
	/// <param name="texture">The texture to draw. If null or invalid, draws the styled box without a texture.</param>
	/// <param name="defaultSize">Controls how the texture is sized within the panel (e.g. <see cref="Length.Cover"/>, <see cref="Length.Contain"/>, <see cref="Length.Auto"/>).</param>
	protected void DrawBackgroundTexture( Texture texture, Length defaultSize )
	{
		var style = ComputedStyle;
		if ( style == null ) return;

		if ( texture == Texture.Invalid )
			texture = null;

		var opacity = CachedRenderOpacity;
		var rect = Box.Rect;
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
			OverrideBlendMode = CachedOverrideBlendMode,
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

		if ( texture != null )
		{
			desc.BackgroundImage = texture;
			desc.BackgroundRect = ImageRect.Calculate( new ImageRect.Input
			{
				ScaleToScreen = ScaleToScreen,
				Image = texture,
				PanelRect = rect,
				DefaultSize = defaultSize,
				ImagePositionX = style.BackgroundPositionX,
				ImagePositionY = style.BackgroundPositionY,
				ImageSizeX = style.BackgroundSizeX,
				ImageSizeY = style.BackgroundSizeY,
			} ).Rect;
		}

		CachedDescriptors.Boxes.Add( desc );
	}
}
