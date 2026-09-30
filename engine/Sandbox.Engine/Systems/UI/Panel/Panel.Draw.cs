using Sandbox.Rendering;

namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// To be used inside <see cref="OnDraw()"/> to add custom shapes, textures and text to a panel.
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
	[Obsolete( "Override OnDraw(Painter painter) and use the supplied Painter instead." )]
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
			LegacyPaint.Current.Painter.LegacyRect( rect, color, new Vector4( cornerRadius ) );
		}

		/// <summary>
		/// Draws a filled rectangle with per-corner radius control.
		/// </summary>
		/// <param name="rect">The rectangle to draw, in panel-local coordinates.</param>
		/// <param name="color">Fill color.</param>
		/// <param name="cornerRadius">Corner radii as (bottom-right, top-right, bottom-left, top-left).</param>
		public static void Rect( Rect rect, Color color, Vector4 cornerRadius )
		{
			LegacyPaint.Current.Painter.LegacyRect( rect, color, cornerRadius );
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
			LegacyPaint.Current.Painter.LegacyTexture( texture, rect, tint ?? Color.White );
		}

		// Text is built into this and handed straight to the draw buffer, so one list per thread serves every call

		/// <summary>
		/// Draws a text string within the given rectangle.
		/// </summary>
		/// <param name="text">The text to render.</param>
		/// <param name="rect">Bounding rectangle for text layout, in panel-local coordinates.</param>
		/// <param name="size">Font size in pixels.</param>
		/// <param name="color">Text color.</param>
		/// <param name="flags">Text alignment and layout flags. Defaults to <see cref="TextFlag.LeftTop"/>.</param>
		/// <param name="font">Font family name. Defaults to "Roboto".</param>
		public static void Text( string text, Rect rect, float size, Color color, TextFlag flags = TextFlag.LeftTop, string font = "Roboto" )
			=> LegacyPaint.Current.Painter.LegacyText( text, rect, new TextStyle { FontSize = size, Color = color, Alignment = flags, FontName = font } );

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
			LegacyPaint.Current.Painter.LegacyShadow( rect, color, blur, spread, offset, cornerRadius, inset );
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
			LegacyPaint.Current.Painter.LegacyOutline( rect, color, width, cornerRadius, offset );
		}
	}

}
