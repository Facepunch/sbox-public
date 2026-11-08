namespace Sandbox.Engine.Settings;

[Expose]
public enum VolumetricFogQuality
{
	/// <summary>No fog volume at all.</summary>
	Off = -1,

	Low = 0,
	Medium = 1,
	High = 2,

	/// <summary>Same as <see cref="High"/>, kept so existing code and saved settings still resolve.</summary>
	Ultra = 3
}
