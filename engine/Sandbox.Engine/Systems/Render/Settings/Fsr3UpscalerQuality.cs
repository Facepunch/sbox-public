namespace Sandbox.Engine.Settings;

/// <summary>
/// Controls the upscaler quality preset which determines the internal render resolution.
/// Higher quality = higher resolution = better image but lower performance.
/// </summary>
public enum Fsr3UpscalerQuality
{
	/// <summary>
	/// No upscaling, render at native resolution
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
	/// Renders at native resolution. FSR3 still runs to provide temporal anti-aliasing.
	/// </summary>
	Native = 4
}
