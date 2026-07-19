namespace Sandbox;

/// <summary>
/// Makes every <see cref="SpriteRenderer"/> beneath this object sort as one unit. The whole group
/// takes its place in the draw order from this component's own layer and order, and its sprites
/// sort among themselves by their individual sort orders - so nothing from outside the group can
/// ever be drawn in between them.
///
/// This is what keeps a character built out of separate sprites from being sliced apart by a tree
/// or another character standing at a similar depth.
/// </summary>
[Expose]
[Title( "Sorting Group" )]
[Category( "Rendering" )]
[Icon( "layers" )]
public sealed class SortingGroup : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// Which sort layer the whole group sits in. The sort layer on the individual sprites beneath
	/// this group is ignored.
	/// </summary>
	[Property, Category( "Sorting" ), Order( -150 )]
	[Editor( "sortlayer" )]
	public SortLayerHandle SortLayer { get; set; }

	/// <summary>
	/// Draw order of the whole group within its layer. Higher draws on top. The sort order on the
	/// individual sprites beneath this group orders them against each other, not against the world.
	/// </summary>
	[Property, Category( "Sorting" ), Order( -149 )]
	public int SortOrder { get; set; }

	/// <summary>
	/// The number of sprite members, so the inspector can say whether this group is doing anything.
	/// </summary>
	public int MemberCount => GetComponentsInChildren<SpriteRenderer>( true ).Count();

	/// <summary>
	/// The group a sprite actually belongs to: the nearest enabled one at or above it.
	///
	/// Nesting is deliberately not supported - the draw order has room for one group's worth of
	/// ordering, not a tree of it - so an inner group simply wins outright for the sprites beneath
	/// it and the outer group never sees them.
	/// </summary>
	internal static SortingGroup FindFor( SpriteRenderer sprite )
		=> sprite.GetComponentInParent<SortingGroup>();

	/// <summary>
	/// True when this group is inside another one. Reported in the editor rather than fixed,
	/// because silently flattening someone's hierarchy is worse than telling them it won't nest.
	/// </summary>
	public bool IsNested => GameObject.Parent?.GetComponentInParent<SortingGroup>() is not null;

	/// <summary>
	/// The largest rank the draw order can represent. Members past this all share the last rank
	/// and fall back to sorting against each other by depth.
	/// </summary>
	internal const int MaxRank = 255;

	private readonly Dictionary<Guid, int> _ranks = new();
	private readonly List<SpriteRenderer> _rankScratch = new();

	/// <summary>
	/// Rebuilds the member ordering. The draw order only has room for a small dense rank per
	/// member, so the authored sort orders - which may be any integers at all, with gaps - are
	/// compressed down to 0, 1, 2... in the same relative order.
	///
	/// Call this before reading <see cref="GetRank"/> from parallel code; it is not thread safe,
	/// but it is deterministic, so calling it more than once in a frame is harmless.
	/// </summary>
	internal void RefreshRanks()
	{
		_ranks.Clear();
		_rankScratch.Clear();

		foreach ( var sprite in GetComponentsInChildren<SpriteRenderer>( true ) )
		{
			// A sprite under a nested group belongs to that group, not to this one.
			if ( FindFor( sprite ) != this ) continue;

			_rankScratch.Add( sprite );
		}

		// Id breaks ties so that two members sharing a sort order keep a stable order between
		// frames. Without it they would swap according to component iteration order and flicker.
		_rankScratch.Sort( ( a, b ) =>
		{
			var byOrder = a.SortOrder.CompareTo( b.SortOrder );
			return byOrder != 0 ? byOrder : a.Id.CompareTo( b.Id );
		} );

		for ( var i = 0; i < _rankScratch.Count; i++ )
		{
			_ranks[_rankScratch[i].Id] = Math.Min( i, MaxRank );
		}

		_rankScratch.Clear();
	}

	/// <summary>
	/// This sprite's position in the group's internal draw order. Higher draws in front.
	/// </summary>
	internal int GetRank( SpriteRenderer sprite )
		=> _ranks.TryGetValue( sprite.Id, out var rank ) ? rank : 0;
}
