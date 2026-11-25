namespace Nav;

/// <summary>
/// What AgentParams.Create needs. Every field is chosen by the caller. Lengths are in the mesh's units,
/// speeds in units per time unit of Step's delta.
/// </summary>
public struct AgentDesc
{
	public required float Radius, Height, MaxSpeed, Acceleration;
}
