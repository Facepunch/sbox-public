using System.Numerics;

namespace Nav;

/// <summary>
/// What the crowd remembers about an agent between steps, besides its numbers in Agent.
///
/// Four facts say what the agent is doing. AgentStatus is read off them and AgentState.Link:
///
///   OnLink       Link is set
///   Pending      HasTarget, no HasRoute, NeedsPlan
///   Unreachable  HasTarget, no HasRoute, and no plan to come
///   Blocked      HasTarget, HasRoute, AtEnd (the route is partial)
///   Moving       HasTarget, HasRoute
///   Arrived      no HasTarget, AtEnd (RouteEnd is the rest point)
///   Idle         none of these
///
/// Three chores are left for the next plan stage, which does them in the order Validate, NeedsRepair,
/// NeedsPlan and clears each as it goes.
/// </summary>
[Flags]
internal enum AgentFlags : ushort
{
	Active = 1 << 0,

	// MoveTo or SetPath asked for AgentState.Target and it has not been reached.
	HasTarget = 1 << 1,

	// The corridor leads to RouteEnd. A new target keeps the old route until the next plan replaces it.
	HasRoute = 1 << 2,

	// The route stops short of the target it was planned for.
	Partial = 1 << 3,

	// The agent has reached RouteEnd. It returns there when pushed away by more than a quarter radius.
	AtEnd = 1 << 4,

	// The mesh or the filter changed: check the corridor's entries.
	Validate = 1 << 5,

	// The corridor does not lead on from where the agent is: attach it again. Either the caller moved
	// the agent, and AgentState.Poly is none until the plan has found its polygon, or a move stepped
	// out of the corridor, and it is joined again further on instead of walking back in.
	NeedsRepair = 1 << 6,

	// Find the polygon under the agent if it has none, and search if it has somewhere to go. Set by a
	// new target, a changed filter or mesh and a lost corridor. A failed attempt clears it, so it is
	// not tried again until one of those happens.
	NeedsPlan = 1 << 7,

	// External only: the caller has given a velocity with SetVelocity, so the crowd does not derive one.
	OwnVelocity = 1 << 8
}

internal struct Agent
{
	public AgentFlags Flags;

	// The corridor is the first Count entries of the agent's array, starting with the polygon it
	// stands on. RouteEnd is where it leads, on the mesh, and the rest point once the agent has arrived.
	public int Count;
	public Vector3 RouteEnd;

	// The corners of the last substep: how many (none once the corridor is replaced), the polygon and
	// corridor length they were pulled for, and from where. End is what they lead to.
	public int CornerCount, CornerSpan;
	public PolyRef CornerPoly;
	public Vector2 CornerFrom;

	// The walls around WallCenter. A count of -1 says collect them again.
	public int WallCount;
	public Vector3 WallCenter;

	// The Step in which the agent last entered or left a link. One link transition per Step.
	public uint LinkStep;

	// The contents of the filter's cost array as SetParams last saw them.
	public uint CostKey;

	// External only: where the agent was at the last Step, to tell its velocity.
	public Vector3 Seen;

	// What the agent heads for: the next link entry, or RouteEnd.
	public Vector3 End;

	// The rest is one substep's scratch. From the plan: how far the next corner is, and how far the
	// agent may go: to the end of its route, or without limit towards a link. A negative Stride says
	// it does not walk.
	public float CornerDistance, Stride;

	// From the separate stage: the push it has collected.
	public Vector2 Push;

	public readonly bool Has( AgentFlags flags ) => (Flags & flags) != 0;

	// The agent is not where its corridor starts, or the mesh under it is gone: forget the corridor
	// and look for the mesh in the next plan.
	public void Unseat()
	{
		Count = 0;
		CornerCount = 0;
		WallCount = -1;
		Flags = Flags & ~(AgentFlags.HasRoute | AgentFlags.Partial | AgentFlags.NeedsRepair) | AgentFlags.NeedsPlan;
	}
}

// What neighbours see of the agents in the grid during a substep, one array per value so that eight
// neighbours can be tested at once.
internal readonly struct Frame
{
	public readonly float[] X, Y, Z, Top, Radius, VelocityX, VelocityY;
	public readonly int[] Id;
	public readonly bool[] Yields;

	public Frame( int capacity )
	{
		X = new float[capacity];
		Y = new float[capacity];
		Z = new float[capacity];
		Top = new float[capacity];
		Radius = new float[capacity];
		VelocityX = new float[capacity];
		VelocityY = new float[capacity];
		Id = new int[capacity];
		Yields = new bool[capacity];
	}

	public int Capacity => X?.Length ?? 0;
}
