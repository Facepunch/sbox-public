namespace Nav;

/// <summary>
/// What an agent is. Lengths are in the mesh's units, speeds in units per time unit of Step's delta.
/// </summary>
public struct AgentParams
{
	/// <summary>
	/// Acceleration is how fast the agent's speed changes. Its heading turns as fast as that allows across its
	/// way, and at least by MaxSpeed / (1.5 x Radius) per time unit, so that it takes a tight turn without
	/// slowing to a stop.
	/// </summary>
	public float Radius, Height, MaxSpeed, Acceleration;

	/// <summary>
	/// 0 to 1: how strongly the agent keeps its distance from neighbours closer than two combined radii.
	/// </summary>
	public float Separation;

	/// <summary>
	/// The crowd takes the agent across links: an agent it moves is walked along the link, speeding up
	/// from the speed it arrived with to MaxSpeed, and leaves it with that speed along the ground;
	/// for an External agent DesiredVelocity points at the link's far end, and the link ends when the
	/// caller has brought the agent within its radius of that. Without AutoLinks the agent stays
	/// OnLink until CompleteLink.
	/// </summary>
	public bool AutoLinks;

	/// <summary>
	/// Who moves the agent: the crowd, or its owner with the crowd steering it or only planning for it.
	/// </summary>
	public AgentMode Mode;

	// Its owner moves it: Steered or Planned. An External agent in the docs.
	internal readonly bool External => Mode != AgentMode.Moved;

	internal readonly bool Steered => Mode == AgentMode.Steered;

	/// <summary>
	/// The agent type of the mesh whose tiles the agent plans and moves on. Create sets 0, the first;
	/// the crowd refuses a type its mesh does not have.
	/// </summary>
	public byte AgentType;

	// Last, so that the fields a step reads share a cache line.
	public NavFilter Filter;

	/// <summary>
	/// The fields of desc, Filter from NavFilter.Create, Separation 0.25, AutoLinks true, Mode Moved,
	/// AgentType 0.
	/// </summary>
	public static AgentParams Create( in AgentDesc desc )
	{
		return new AgentParams { Radius = desc.Radius, Height = desc.Height, MaxSpeed = desc.MaxSpeed, Acceleration = desc.Acceleration, Separation = 0.25f, Filter = NavFilter.Create(), AutoLinks = true, Mode = AgentMode.Moved, AgentType = 0 };
	}

	/// <summary>
	/// False, with the reason, for params the crowd refuses: Radius and Height must be finite and
	/// above 0, MaxSpeed and Acceleration finite and not negative, Separation from 0 to 1, Mode an
	/// AgentMode, and the filter one made with NavFilter.Create.
	/// </summary>
	public readonly bool IsValid( out string? reason )
	{
		reason = null;
		if ( !(Radius > 0) || !float.IsFinite( Radius ) ) reason = $"Radius {Radius} is not a finite number above 0";
		else if ( !(Height > 0) || !float.IsFinite( Height ) ) reason = $"Height {Height} is not a finite number above 0";
		else if ( !(MaxSpeed >= 0) || !float.IsFinite( MaxSpeed ) ) reason = $"MaxSpeed {MaxSpeed} is not a finite number of at least 0";
		else if ( !(Acceleration >= 0) || !float.IsFinite( Acceleration ) ) reason = $"Acceleration {Acceleration} is not a finite number of at least 0";
		else if ( !(Separation >= 0) || Separation > 1 ) reason = $"Separation {Separation} is not from 0 to 1";
		else if ( Mode > AgentMode.Planned ) reason = $"Mode {(byte)Mode} is not an AgentMode";
		else Filter.IsValid( out reason );
		return reason == null;
	}
}
