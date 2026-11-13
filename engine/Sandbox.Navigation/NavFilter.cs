namespace Nav;

/// <summary>
/// Which area ids a traveller may cross and what each costs. The default allows everything at cost 1.
/// </summary>
public readonly struct NavFilter
{
	/// <summary>
	/// Area ids run from 0 to MaxArea. NoArea is not walkable: a volume with it blocks, and no polygon
	/// carries it. Ground that no volume marks is DefaultArea, and so is a link that names no area.
	/// </summary>
	public const byte NoArea = 0, DefaultArea = 1, MaxArea = 63;

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
	/// Throws for a cost that is below 1 or not a finite number: a search takes the straight distance
	/// for the least a path can cost, and a cheaper area would make its paths wrong.
	/// </summary>
	public NavFilter( ulong forbidden, float[]? costs = null )
	{
		if ( costs != null )
		{
			foreach ( float cost in costs )
			{
				if ( !(cost >= 1) || float.IsInfinity( cost ) ) throw new ArgumentOutOfRangeException( nameof( costs ), $"Area cost {cost} is not a finite number of at least 1" );
			}
		}
		Forbidden = forbidden;
		Costs = costs;
	}

	internal bool Allows( int area ) => (Forbidden >> area & 1) == 0;

	internal float Cost( int area )
	{
		var costs = Costs;
		return costs != null && (uint)area < (uint)costs.Length ? costs[area] : 1f;
	}
}
