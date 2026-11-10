namespace Sandbox.Rendering;

public enum Stage
{
	AfterDepthPrepass = 1000,
	AfterOpaque = 2000,
	AfterSkybox = 3000,
	AfterTransparent = 4000,
	AfterViewmodel = 5000,

	/// <summary>
	/// The screen-space UI layer that renders before post-processing. Blends in gamma space: write sRGB-encoded color
	/// (the UI shaders do; custom shaders call UIEncodeOutput from ui/gamma.hlsl).
	/// </summary>
	EarlyUI = 5500,

	BeforePostProcess = 6000,
	Tonemapping = 6500,
	AfterPostProcess = 7000,

	/// <summary>
	/// The screen-space UI layer. Blends in gamma space like a browser: write sRGB-encoded color
	/// (the UI shaders do; custom shaders call UIEncodeOutput from ui/gamma.hlsl).
	/// </summary>
	UI = 7500,

	AfterUI = 8000,
}
