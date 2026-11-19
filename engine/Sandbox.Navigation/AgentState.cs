using System.Numerics;

namespace Nav;

/// <summary>
/// Where an agent is. Written by the crowd, read through NavCrowd.States.
/// </summary>
public struct AgentState
{
	/// <summary>
	/// Position is on the polygon plane while the agent is on the mesh; for an External agent it is
	/// exactly what Add or SetPosition gave. Velocity is the movement of the last substep, before
	/// neighbours were pushed apart; for an External agent see SetPosition. DesiredVelocity points
	/// along the route at the speed the agent should have; on a link that an External agent is
	/// taken across it points at the link's far end, and it is zero when there is nowhere to go.
	/// </summary>
	public Vector3 Position, Velocity, DesiredVelocity;

	/// <summary>
	/// As last requested. Meaningful while Status is Pending, Moving, OnLink, Blocked or Unreachable.
	/// </summary>
	public Vector3 Target;

	/// <summary>
	/// The target the route being followed was planned for. Differs from Target until a new target is planned.
	/// </summary>
	public Vector3 RouteTarget;

	public AgentStatus Status;

	/// <summary>
	/// The polygon the agent stands on. None off the mesh, on a link, and from SetPosition to the next Step.
	/// </summary>
	public PolyRef Poly;

	/// <summary>
	/// How many times Add has handed out this id. An id and the generation read after Add name one
	/// agent for good: when either the Status is Free or the generation has moved on, it is gone.
	/// </summary>
	public uint Generation;

	/// <summary>
	/// The link id while OnLink, else -1. Between two links the agent is off the link for at least one
	/// Step, also when a call ended the first, so comparing States between Steps shows every entry and exit.
	/// </summary>
	public int Link;

	/// <summary>
	/// The ends of the link in travel direction and where the agent stood when it entered.
	/// </summary>
	public Vector3 LinkStart, LinkEnd, LinkEntry;
}
