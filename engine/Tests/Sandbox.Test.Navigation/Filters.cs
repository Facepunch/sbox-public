namespace Nav.Lib.Tests;

/// <summary>
/// Filters for tests: everything allowed at cost 1, and arrays of costs set in one go.
/// </summary>
static class Filters
{
	public static readonly NavFilter All = NavFilter.Create();

	public static NavFilter WithAreaCosts( this NavFilter filter, ReadOnlySpan<float> costs )
	{
		for ( int i = 0; i < costs.Length; i++ ) filter = filter.WithAreaCost( i, costs[i] );
		return filter;
	}

	public static NavFilter WithLinkCosts( this NavFilter filter, ReadOnlySpan<float> costs )
	{
		for ( int i = 0; i < costs.Length; i++ ) filter = filter.WithLinkCost( i, costs[i] );
		return filter;
	}

	public static NavFilter WithLinkPenalties( this NavFilter filter, ReadOnlySpan<float> penalties )
	{
		for ( int i = 0; i < penalties.Length; i++ ) filter = filter.WithLinkPenalty( i, penalties[i] );
		return filter;
	}
}
