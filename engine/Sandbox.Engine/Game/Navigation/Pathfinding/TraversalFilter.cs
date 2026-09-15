namespace Sandbox.Navigation.Pathfinding;

/// <summary>Immutable area permissions and multipliers captured before a query or simulation update.</summary>
internal readonly struct TraversalFilter
{
	internal static readonly TraversalFilter Unrestricted = new( uint.MaxValue, null );
	private readonly uint allowedAreas;
	private readonly float[] areaCosts;

	internal TraversalFilter( uint allowedAreas, float[] areaCosts )
	{
		this.allowedAreas = allowedAreas;
		this.areaCosts = areaCosts;
	}

	internal bool Allows( int area ) => (allowedAreas & (1u << area)) != 0;
	internal float CostMultiplier( int area ) => areaCosts?[area] ?? 1;
	internal float Cost( Vector3 from, Vector3 to, int area ) => Vector3.DistanceBetween( from, to ) * CostMultiplier( area );
}
