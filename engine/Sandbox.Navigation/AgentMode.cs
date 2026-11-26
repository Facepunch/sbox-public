namespace Nav;

/// <summary>
/// Who moves an agent, and so what the crowd does for it.
/// </summary>
public enum AgentMode : byte
{
	/// <summary>The crowd moves it: plans, steers round neighbours and walls, walks it on the mesh and across links.</summary>
	Moved,

	/// <summary>
	/// Its owner moves it, telling the crowd where it is with NavCrowd.SetPosition every tick. The crowd plans and steers
	/// it as one it moves, at the agent's own Acceleration and turn rate, and publishes AgentState.SteeredVelocity for
	/// the owner to move it at and AgentState.Push, separation's push, to move it by once after the Step. It steers from
	/// the velocity it told the agent last, not from how the owner moved it, so an owner that lags, is held up or
	/// teleports does not change its turns; NavCrowd.SetVelocity tells it of a knock. Neighbours avoid it as one that
	/// avoids them too, and leave it its half of an overlap.
	/// </summary>
	Steered,

	/// <summary>
	/// Its owner moves it, telling the crowd where it is with NavCrowd.SetPosition every tick. The crowd plans for it
	/// and publishes DesiredVelocity, and never moves, steers or pushes it. Others avoid it wherever it is, on the mesh
	/// or not, as long as it overlaps them in height: a player, a physics prop.
	/// </summary>
	Planned
}
