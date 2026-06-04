namespace Sandbox.Engine.Settings;

/// <summary>
/// Selects which upscaler is used to take a lower-resolution scene render and upscale it
/// back to display resolution. Mirrors the <c>r_upscaling</c> ConVar.
/// </summary>
public enum UpscalerMode
{
	/// <summary>Native resolution rendering, no upscaler.</summary>
	Off = 0,

	/// <summary>Render at lower resolution, bilinear blit to display resolution.</summary>
	Stretch = 1,

	/// <summary>AMD FidelityFX Super Resolution 1 — spatial upscaler (EASU + RCAS).</summary>
	FSR1 = 2,

	/// <summary>AMD FidelityFX Super Resolution 3 — temporal upscaler.</summary>
	FSR3 = 3,
}
