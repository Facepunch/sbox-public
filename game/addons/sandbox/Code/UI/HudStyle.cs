namespace Sandbox;

/// <summary>
/// The look of the HUD (font, colours, panels, HDR glow), shared by the HUD sections and the weapon screens
/// </summary>
public static class HudStyle
{
	public const string Font = "Inconsolata";

	/// <summary>
	/// Background colour of HUD panels
	/// </summary>
	public static readonly Color PanelColor = new( 0.07f, 0.5f );

	/// <summary>
	/// Small captions like HEALTH and AMMO
	/// </summary>
	public static readonly Color TitleColor = new( 0.925f, 0.925f, 0.925f );

	/// <summary>
	/// Scales the colour, leaving its alpha alone. A gain past 1 goes over 1 for HDR glow, below 1 darkens.
	/// </summary>
	public static Color Brightened( Color color, float gain ) => (color * gain).WithAlpha( color.a );

	const float PanelRadius = 2;
	const float PanelSoftness = 0.5f;

	/// <summary>
	/// A flat panel with rounded corners
	/// </summary>
	public static void DrawPanel( Painter painter, Rect rect, Color color, float scale )
	{
		var softness = PanelSoftness * scale;
		var radius = PanelRadius * scale;

		painter.RectShadow( rect, radius, color, softness * 2, softness );

		painter.Fill = color;
		painter.Rect( rect, radius );
	}

	/// <summary>
	/// The HUD's tint and HDR gains, for things painted outside the HUD canvas like the weapon screens
	/// </summary>
	public readonly record struct Look( Color Tint, float TextGain, float DigitGain )
	{
		/// <summary>
		/// Tinted text at the regular HUD brightness
		/// </summary>
		public Color Text( Color color ) => Brightened( color * Tint, TextGain );

		/// <summary>
		/// Tinted and pushed into HDR like the HUD digits, so it glows
		/// </summary>
		public Color Glow( Color color ) => Brightened( color * Tint, DigitGain );
	}

	public static Look For( Scene scene )
	{
		var layer = scene?.Get<HudLayer>();
		return layer.IsValid() ? new Look( layer.CurrentTint, layer.TextGain, layer.DigitGain ) : new Look( HudLayer.DefaultTint, 1, 1 );
	}
}
