namespace Nav;

/// <summary>
/// What an agent is. Lengths are in the mesh's units, speeds in units per time unit of Step's delta.
/// </summary>
public struct AgentParams
{
	public float Radius, Height, MaxSpeed, Acceleration;

	/// <summary>
	/// 0 to 1: how strongly the agent keeps its distance from neighbours closer than two combined radii.
	/// </summary>
	public float Separation;

	public NavFilter Filter;

	/// <summary>
	/// The crowd takes the agent across links: an agent it moves is walked along the link at MaxSpeed;
	/// for an External agent DesiredVelocity points at the link's far end, and the link ends when the
	/// caller has brought the agent within its radius of that. Without AutoLinks the agent stays
	/// OnLink until CompleteLink.
	/// </summary>
	public bool AutoLinks;

	/// <summary>
	/// The caller moves this agent, see NavCrowd.SetPosition. The crowd plans for it and publishes
	/// DesiredVelocity, and never moves or pushes it. Others avoid it wherever it is, on the mesh or
	/// not, as long as it overlaps them in height.
	/// </summary>
	public bool External;

	public static AgentParams Create( float radius, float height, float maxSpeed, float acceleration )
	{
		return new AgentParams { Radius = radius, Height = height, MaxSpeed = maxSpeed, Acceleration = acceleration, Separation = 0.25f, AutoLinks = true };
	}
}
