namespace Sandbox.PanelGallery;

/// <summary>
/// Shared accent colours for Gallery examples. Keep the SCSS tokens in sync.
/// </summary>
public static class GalleryPalette
{
	/// <summary>
	/// Cyan accent as a hex string, for CSS and string contexts.
	/// </summary>
	public const string CyanHex = "#29D8FF";

	/// <summary>
	/// Pink accent as a hex string, for CSS and string contexts.
	/// </summary>
	public const string PinkHex = "#FF4F79";

	/// <summary>
	/// Lime accent as a hex string, for CSS and string contexts.
	/// </summary>
	public const string LimeHex = "#C8FF4A";

	/// <summary>
	/// Cyan accent as a colour.
	/// </summary>
	public static readonly Color Cyan = CyanHex;

	/// <summary>
	/// Pink accent as a colour.
	/// </summary>
	public static readonly Color Pink = PinkHex;

	/// <summary>
	/// Lime accent as a colour.
	/// </summary>
	public static readonly Color Lime = LimeHex;
}
