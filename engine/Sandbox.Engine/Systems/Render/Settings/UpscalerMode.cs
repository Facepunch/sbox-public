using System.ComponentModel;

namespace Sandbox.Engine.Settings;

/// <summary>
/// Selects which upscaler is used to take a lower-resolution scene render and upscale it
/// back to display resolution. Mirrors the <c>r_upscaling</c> ConVar.
/// </summary>
public enum UpscalerMode
{
	/// <summary>No dedicated upscaler; reduced render scale uses a simple blit.</summary>
	Off = 0,

	/// <summary>Legacy selection, treated as Off. Retained for existing settings and addons.</summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	Stretch = 1,

	/// <summary>AMD FidelityFX Super Resolution 1 — spatial upscaler (EASU + RCAS).</summary>
	FSR1 = 2,

	/// <summary>AMD FidelityFX Super Resolution 3 — temporal upscaler.</summary>
	FSR3 = 3,

	/// <summary>NVIDIA Deep Learning Super Sampling — temporal AI upscaler. NVIDIA hardware only.</summary>
	DLSS = 4,
}
