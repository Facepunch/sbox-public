namespace Nav;

/// <summary>
/// What an agent is doing, in AgentState.Status. The crowd works it out from whether the agent is on
/// a link, has a target, has a route and has reached the route's end, each time one of those may
/// have changed: in a Step and in every call that concerns the agent.
/// </summary>
public enum AgentStatus : byte
{
	/// <summary>
	/// The id is not in use.
	/// </summary>
	Free,

	/// <summary>
	/// No target and nowhere to be.
	/// </summary>
	Idle,

	/// <summary>
	/// Has a target that has not been planned for yet. The next Step settles it.
	/// </summary>
	Pending,

	/// <summary>
	/// Following a route. Stays Moving when the target changes, until the new route replaces the old one.
	/// </summary>
	Moving,

	/// <summary>
	/// On a link. With AutoLinks the crowd takes the agent across, see AgentParams.AutoLinks; otherwise
	/// it waits for CompleteLink.
	/// </summary>
	OnLink,

	/// <summary>
	/// Reached the end of a complete route. The target is cleared; the agent returns here when pushed away.
	/// </summary>
	Arrived,

	/// <summary>
	/// At the end of a route that stops short of the target. The target is kept and planned again when the mesh changes.
	/// </summary>
	Blocked,

	/// <summary>
	/// Has a target and no way to it: the agent is off the mesh, the target is further from the mesh
	/// than TargetExtents, or no route was found. The target is kept and nothing more is tried until
	/// something changes. MoveTo and SetPosition make it Pending again. SetParams does so with a
	/// changed filter, and with anything while the agent is off the mesh; a new size or speed alone
	/// changes nothing for an agent on the mesh, and neither does a new TargetExtents. A changed
	/// mesh is planned on in the next Step. Stop and SetPath end it.
	/// </summary>
	Unreachable
}
