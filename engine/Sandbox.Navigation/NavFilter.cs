namespace Nav;

/// <summary>
/// Which area ids and link types a traveller may cross and what each costs. The default allows
/// everything at cost 1.
/// </summary>
public readonly struct NavFilter
{
	/// <summary>
	/// Area ids run from 0 to MaxArea. NoArea is not walkable: a volume with it blocks, and no polygon
	/// carries it. Ground that no volume marks is DefaultArea, and so is a link that names no area.
	/// </summary>
	public const byte NoArea = 0, DefaultArea = 1, MaxArea = 63;

	/// <summary>
	/// Link types run from 0, the type of a link that names none, to MaxLinkType.
	/// </summary>
	public const byte MaxLinkType = 63;

	/// <summary>
	/// One bit per area id, set means the area cannot be crossed.
	/// </summary>
	public readonly ulong Forbidden;

	/// <summary>
	/// Cost multiplier per area id, at least 1. Null or a missing entry means 1. The array is not
	/// copied: keep every entry at 1 or above when changing it later.
	/// </summary>
	public readonly float[]? Costs;

	/// <summary>
	/// One bit per link type, set means links of the type cannot be taken.
	/// </summary>
	public readonly ulong ForbiddenLinks;

	/// <summary>
	/// Cost multiplier per link type, at least 1, on top of the cost of the link's area. Null or a
	/// missing entry means 1. Not copied, as Costs.
	/// </summary>
	public readonly float[]? LinkCosts;

	/// <summary>
	/// Added once per crossing, per link type, in the mesh's units and at least 0: how much further
	/// the traveller walks to avoid the link. Null or a missing entry means 0. Not copied, as Costs.
	/// </summary>
	public readonly float[]? LinkPenalties;

	/// <summary>
	/// Throws for a cost below 1, a penalty below 0 and anything that is not a finite number: a search
	/// takes the straight distance for the least a path can cost, and a cheaper area or link would
	/// make its paths wrong.
	/// </summary>
	public NavFilter( ulong forbidden, float[]? costs = null, ulong forbiddenLinks = 0, float[]? linkCosts = null, float[]? linkPenalties = null )
	{
		Check( costs, 1, nameof( costs ), "Area cost" );
		Check( linkCosts, 1, nameof( linkCosts ), "Link cost" );
		Check( linkPenalties, 0, nameof( linkPenalties ), "Link penalty" );
		Forbidden = forbidden;
		Costs = costs;
		ForbiddenLinks = forbiddenLinks;
		LinkCosts = linkCosts;
		LinkPenalties = linkPenalties;
	}

	static void Check( float[]? values, float least, string name, string what )
	{
		foreach ( float value in values ?? [] )
		{
			if ( !(value >= least) || float.IsInfinity( value ) ) throw new ArgumentOutOfRangeException( name, $"{what} {value} is not a finite number of at least {least}" );
		}
	}

	internal bool Allows( int area ) => (Forbidden >> area & 1) == 0;

	internal bool AllowsLink( int type ) => (ForbiddenLinks >> type & 1) == 0;

	internal float Cost( int area )
	{
		var costs = Costs;
		return costs != null && (uint)area < (uint)costs.Length ? costs[area] : 1f;
	}

	internal float LinkCost( int type )
	{
		var costs = LinkCosts;
		return costs != null && (uint)type < (uint)costs.Length ? costs[type] : 1f;
	}

	internal float LinkPenalty( int type )
	{
		var penalties = LinkPenalties;
		return penalties != null && (uint)type < (uint)penalties.Length ? penalties[type] : 0f;
	}
}
