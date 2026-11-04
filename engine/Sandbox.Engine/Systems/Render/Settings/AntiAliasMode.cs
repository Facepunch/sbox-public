namespace Sandbox.Engine.Settings;

/// <summary>
/// Selects whether the completed camera image is filtered with CMAA2.
/// Hardware multisampling is configured separately through <see cref="RenderSettings.AntiAliasQuality"/>.
/// </summary>
public enum AntiAliasMode
{
	/// <summary>Rely on hardware multisampling alone.</summary>
	None = 0,
	/// <summary>Filter the completed camera image, on top of any hardware multisampling.</summary>
	Cmaa2 = 1,
}
