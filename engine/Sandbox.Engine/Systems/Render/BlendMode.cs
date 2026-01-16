namespace Sandbox
{
	/// <summary>
	/// Blend modes used by the UI system
	/// </summary>
	[Expose]
	public enum BlendMode
	{
		// these match web stuff. I haven't implemented them all in the shader. See ui/features.hlsl

		Normal = 0,
		Multiply = 1,
		// Screen = 2,
		// Overlay = 3,
		// Darken = 4,
		Lighten = 2,
		PremultipliedAlpha = 3,
		// 5
		// ColorDodge = 6,
		// ColorBurn = 7,
		// HardLight = 8,
		// SoftLight = 9,
		// Difference = 10,
		// Exclusion = 11,




	}
}
