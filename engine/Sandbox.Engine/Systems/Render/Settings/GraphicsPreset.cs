namespace Sandbox.Engine.Settings;

/// <summary>
/// A bundle of the individual quality settings. Changing any of them afterwards leaves you
/// on <see cref="Custom"/>.
/// </summary>
public enum GraphicsPreset
{
	/// <summary>The individual settings don't match any preset.</summary>
	Custom = -1,

	Low = 0,
	Medium = 1,
	High = 2,
	Ultra = 3
}
