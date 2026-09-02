namespace Sandbox.Engine.Settings;

/// <summary>
/// Quality of a single post-processing effect. <see cref="PostProcessQuality"/> is a preset over
/// a bundle of these.
/// </summary>
public enum EffectQuality
{
	/// <summary>The effect doesn't run at all.</summary>
	Off = -1,

	Low = 0,
	Medium = 1,
	High = 2
}
