using System.Runtime.CompilerServices;

namespace Nav;

/// <summary>
/// Which area ids and link types a traveller may cross and what each costs. A value of about 780
/// bytes: pass it with in. Make one with Create, which forbids nothing and prices everything at cost 1,
/// and change it with the With methods, which check what they are given. A default NavFilter is not a
/// filter: queries and the crowd throw for it. Two filters are equal when every field is.
/// </summary>
public readonly struct NavFilter : IEquatable<NavFilter>
{
	/// <summary>
	/// Area ids run from 0 to MaxArea. NoArea is not walkable: a volume with it blocks, and no polygon
	/// or link carries it. Ground that no volume marks is DefaultArea.
	/// </summary>
	public const byte NoArea = 0, DefaultArea = 1, MaxArea = 63;

	/// <summary>
	/// Link types run from 0 to MaxLinkType.
	/// </summary>
	public const byte MaxLinkType = 63;

	/// <summary>
	/// One float per area id or link type.
	/// </summary>
	[InlineArray( 64 )]
	public struct Table
	{
		float first;
	}

	/// <summary>
	/// One bit per area id, set means the area cannot be crossed.
	/// </summary>
	public readonly ulong ForbiddenAreas;

	/// <summary>
	/// One bit per link type, set means links of the type cannot be taken.
	/// </summary>
	public readonly ulong ForbiddenLinks;

	/// <summary>
	/// Cost multiplier per area id, finite and at least 1.
	/// </summary>
	public readonly Table AreaCosts;

	/// <summary>
	/// Cost multiplier per link type, finite and at least 1, on top of the cost of the link's area.
	/// </summary>
	public readonly Table LinkCosts;

	/// <summary>
	/// Added once per crossing, per link type, in the mesh's units, finite and at least 0: how much
	/// further the traveller walks to avoid the link.
	/// </summary>
	public readonly Table LinkPenalties;

	readonly bool valid;

	/// <summary>
	/// Nothing forbidden, every cost 1, every penalty 0.
	/// </summary>
	public static NavFilter Create()
	{
		Table costs = default;
		((Span<float>)costs).Fill( 1f );
		return new NavFilter( 0, 0, costs, costs, default );
	}

	NavFilter( ulong forbiddenAreas, ulong forbiddenLinks, in Table areaCosts, in Table linkCosts, in Table linkPenalties )
	{
		ForbiddenAreas = forbiddenAreas;
		ForbiddenLinks = forbiddenLinks;
		AreaCosts = areaCosts;
		LinkCosts = linkCosts;
		LinkPenalties = linkPenalties;
		valid = true;
	}

	/// <summary>
	/// False, with the reason, for a filter that was not made with Create. Every other filter is valid:
	/// the With methods refuse what would make one invalid.
	/// </summary>
	public bool IsValid( out string? reason )
	{
		reason = valid ? null : "A default NavFilter is not a filter: make one with NavFilter.Create()";
		return valid;
	}

	public NavFilter WithForbiddenAreas( ulong forbidden ) => new( forbidden, ForbiddenLinks, AreaCosts, LinkCosts, LinkPenalties );

	public NavFilter WithForbiddenLinks( ulong forbidden ) => new( ForbiddenAreas, forbidden, AreaCosts, LinkCosts, LinkPenalties );

	/// <summary>
	/// Throws for an id above MaxArea and a cost that is not a finite number of at least 1: a search
	/// takes the straight distance for the least a path can cost, and a cheaper area would make its
	/// paths wrong.
	/// </summary>
	public NavFilter WithAreaCost( int area, float cost )
	{
		var costs = AreaCosts;
		costs[Index( area, MaxArea, nameof( area ) )] = Checked( cost, 1, nameof( cost ), "Area cost" );
		return new( ForbiddenAreas, ForbiddenLinks, costs, LinkCosts, LinkPenalties );
	}

	/// <summary>
	/// Throws for a type above MaxLinkType and a cost that is not a finite number of at least 1.
	/// </summary>
	public NavFilter WithLinkCost( int type, float cost )
	{
		var costs = LinkCosts;
		costs[Index( type, MaxLinkType, nameof( type ) )] = Checked( cost, 1, nameof( cost ), "Link cost" );
		return new( ForbiddenAreas, ForbiddenLinks, AreaCosts, costs, LinkPenalties );
	}

	/// <summary>
	/// Throws for a type above MaxLinkType and a penalty that is not a finite number of at least 0.
	/// </summary>
	public NavFilter WithLinkPenalty( int type, float penalty )
	{
		var penalties = LinkPenalties;
		penalties[Index( type, MaxLinkType, nameof( type ) )] = Checked( penalty, 0, nameof( penalty ), "Link penalty" );
		return new( ForbiddenAreas, ForbiddenLinks, AreaCosts, LinkCosts, penalties );
	}

	static int Index( int value, int most, string name )
	{
		if ( (uint)value > (uint)most ) throw new ArgumentOutOfRangeException( name, $"{value} is not from 0 to {most}" );
		return value;
	}

	static float Checked( float value, float least, string name, string what )
	{
		if ( !(value >= least) || float.IsInfinity( value ) ) throw new ArgumentOutOfRangeException( name, $"{what} {value} is not a finite number of at least {least}" );
		return value;
	}

	// What every query and the crowd call first: one bool.
	internal void Check()
	{
		if ( !valid ) throw new ArgumentException( "A default NavFilter is not a filter: make one with NavFilter.Create()", "filter" );
	}

	internal bool Allows( int area ) => (ForbiddenAreas >> area & 1) == 0;

	internal bool AllowsLink( int type ) => (ForbiddenLinks >> type & 1) == 0;

	public bool Equals( NavFilter other ) => Same( this, other );

	public override bool Equals( object? obj ) => obj is NavFilter other && Same( this, other );

	public override int GetHashCode() => HashCode.Combine( ForbiddenAreas, ForbiddenLinks, AreaCosts[0], LinkCosts[0], LinkPenalties[0], valid );

	public static bool operator ==( in NavFilter a, in NavFilter b ) => Same( a, b );

	public static bool operator !=( in NavFilter a, in NavFilter b ) => !Same( a, b );

	internal static bool Same( in NavFilter a, in NavFilter b )
	{
		return a.valid == b.valid && a.ForbiddenAreas == b.ForbiddenAreas && a.ForbiddenLinks == b.ForbiddenLinks
			&& ((ReadOnlySpan<float>)a.AreaCosts).SequenceEqual( b.AreaCosts )
			&& ((ReadOnlySpan<float>)a.LinkCosts).SequenceEqual( b.LinkCosts )
			&& ((ReadOnlySpan<float>)a.LinkPenalties).SequenceEqual( b.LinkPenalties );
	}
}
