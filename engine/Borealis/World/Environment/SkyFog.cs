namespace Borealis;

/// <summary>
/// How the 2D sky fades into fog (<c>SceneSkyBox.FogParamInfo</c>).
/// </summary>
public enum SkyFogType
{
	/// <summary>No fog over the sky.</summary>
	None,

	/// <summary>Fog by distance.</summary>
	Distance,

	/// <summary>Fog by angle from the horizon.</summary>
	Angular,
}

/// <summary>
/// The sky's fog: its type and the start and end of its minimum and maximum ranges.
/// </summary>
public readonly record struct SkyFog( SkyFogType Type, float MinStart, float MinEnd, float MaxStart, float MaxEnd );
