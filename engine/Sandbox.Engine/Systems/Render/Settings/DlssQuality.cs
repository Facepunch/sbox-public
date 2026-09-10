namespace Sandbox.Engine.Settings;

/// <summary>
/// Controls the DLSS quality preset which determines the internal render resolution.
/// Higher quality = higher resolution = better image but lower performance.
/// Mirrors the <c>r_dlss_quality</c> ConVar. Unlike FSR3, DLSS has no sharpness control
/// (sharpening was deprecated in DLSS).
/// </summary>
public enum DlssQuality
{
	/// <summary>
	/// No upscaling, render at native resolution. Sentinel value — never written to the ConVar.
	/// </summary>
	Off = -1,

	/// <summary>
	/// Renders at 1/3 of the display resolution (highest performance)
	/// </summary>
	UltraPerformance = 0,

	/// <summary>
	/// Renders at 1/2 of the display resolution
	/// </summary>
	Performance = 1,

	/// <summary>
	/// Renders at 1/1.7 of the display resolution
	/// </summary>
	Balanced = 2,

	/// <summary>
	/// Renders at 1/1.5 of the display resolution (best image quality)
	/// </summary>
	Quality = 3,

	/// <summary>
	/// Deep Learning Anti-Aliasing. Renders at native resolution; DLSS still runs to provide
	/// temporal anti-aliasing.
	/// </summary>
	DLAA = 4
}
