namespace Sandbox.Engine.Settings;

/// <summary>
/// Controls the quality level of post processing effects such as:
/// ambient occlusion, depth of field, motion blur and more
/// </summary>
public enum PostProcessQuality
{
	/// <summary>The individual effects don't match any preset.</summary>
	Custom = -2,

	/// <summary>No post processing at all.</summary>
	Off = -1,

	Low = 0,
	Medium = 1,
	High = 2
}
