using System.Numerics;

namespace Nav;

/// <summary>
/// What a mesh generates links for: drops off ledges and jumps across gaps, found and checked on the
/// height field of each tile build. Left at its default nothing is generated and a build costs what it
/// costs without. Distances are in the caller's units; the agent comes from NavSettings.
/// </summary>
public struct NavLinkSettings
{
	/// <summary>
	/// The furthest an agent may fall, and the furthest a jump may reach sideways and upwards.
	/// MaxJumpDistance plus two agent diameters must fit in a tile. A drop is one way; a ledge no higher
	/// than MaxJumpUp also gets a one-way jump up onto it. A jump between ends within MaxJumpUp of each
	/// other in height is two-way, else one way down.
	/// </summary>
	public float MaxDrop, MaxJumpDistance, MaxJumpUp;

	/// <summary>
	/// The distance between links along an edge. 0 stands for two agent diameters.
	/// </summary>
	public float Spacing;


	/// <summary>
	/// NavLink.Type of the generated drops and jumps (up a ledge too), for a filter to forbid or price.
	/// </summary>
	public byte DropType, JumpType;

	/// <summary>
	/// True: the mesh adds what it generates as links of its own. False: it adds nothing and reports
	/// them per tile (NavMesh.GetGeneratedLinks, NavMesh.TakeLinkChanges), for a caller that keeps its
	/// links as objects of its own and adds them with AddLink like any other.
	/// </summary>
	public bool Insert;

	public readonly bool Enabled => MaxDrop > 0 || MaxJumpDistance > 0;

	/// <summary>
	/// The height of the feet t of the way along a jump, 0 to 1: off the start level and falling, or
	/// rising to arrive level, with a hop of a quarter of the distance on top, no lower than the agent's
	/// climb and no higher than MaxJumpUp. What the build checks a jump against.
	/// </summary>
	public readonly float JumpHeight( Vector3 start, Vector3 end, float t, float climb )
	{
		float flat = Vector2.Distance( new Vector2( start.X, start.Y ), new Vector2( end.X, end.Y ) );
		float hop = MathF.Max( climb, MathF.Min( 0.25f * flat, MaxJumpUp ) );
		float dz = end.Z - start.Z;
		return start.Z + (dz < 0 ? dz * t * t : dz * t * (2 - t)) + 4 * hop * t * (1 - t);
	}

	internal readonly float SpacingOf( in NavSettings settings ) => MathF.Max( Spacing > 0 ? Spacing : 4 * settings.AgentRadius, settings.CellSize );

	// How far from an edge point a search goes: across the two eroded bands of a ledge, or a jump.
	internal readonly float Reach( in NavSettings settings ) => MathF.Max( 2 * settings.AgentRadius + (2 * settings.MaxEdgeError + 2) * settings.CellSize, MaxJumpDistance );

	internal readonly bool IsValid( in NavSettings settings, out string? reason )
	{
		reason = null;
		if ( !(MaxDrop >= 0) || !(MaxJumpDistance >= 0) || !(MaxJumpUp >= 0) || !(Spacing >= 0) || !float.IsFinite( MaxDrop + MaxJumpDistance + MaxJumpUp + Spacing ) ) reason = "Link distances must be finite and not negative";
		else if ( DropType > NavFilter.MaxLinkType || JumpType > NavFilter.MaxLinkType ) reason = $"Link types must not be above {NavFilter.MaxLinkType}";
		else if ( Enabled && Reach( settings ) + 4 * settings.AgentRadius > settings.TileSize * settings.CellSize ) reason = "MaxJumpDistance plus two agent diameters must fit in a tile";
		return reason == null;
	}
}
