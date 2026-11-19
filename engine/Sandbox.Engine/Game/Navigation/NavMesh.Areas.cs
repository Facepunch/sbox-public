using Nav;
using Sandbox.Engine.Resources;
using System.Threading;

namespace Sandbox.Navigation;

public sealed partial class NavMesh
{
	// 0 blocks, then ground, links without a definition, and the definitions by rising priority
	internal const int DefaultArea = NavFilter.DefaultArea, LinkArea = 2, FirstDefinitionArea = 3, AreaCount = NavFilter.MaxArea + 1;

	// A published registry is never mutated
	private sealed record AreaRegistry( Dictionary<NavMeshAreaDefinition, int> Ids, NavMeshAreaDefinition[] Definitions );
	private AreaRegistry areaRegistry;
	private float[] areaCosts = Enumerable.Repeat( 1f, AreaCount ).ToArray();

	private readonly List<NavMeshArea> areas = new();
	private readonly List<NavMeshLink> links = new();

	// Link type ids, given out on first use and kept. 0 is the plain link. Published registries and prices are never mutated.
	internal const int LinkTypeCount = NavFilter.MaxLinkType + 1;
	private sealed record LinkTypeRegistry( Dictionary<NavMeshLinkType, int> Ids, NavMeshLinkType[] Types );
	private sealed record LinkPrices( float[] Costs, float[] Penalties );
	private LinkTypeRegistry linkTypes = new( new(), new NavMeshLinkType[LinkTypeCount] );
	private LinkPrices linkPrices = new( Enumerable.Repeat( 1f, LinkTypeCount ).ToArray(), new float[LinkTypeCount] );
	private readonly object linkTypeGate = new();
	private bool linkTypesFull;

	internal void UpdateAreaIds()
	{
		var ids = new Dictionary<NavMeshAreaDefinition, int>();
		var definitions = new NavMeshAreaDefinition[AreaCount];
		int nextId = FirstDefinitionArea;
		foreach ( var definition in ResourceLibrary.GetAll<NavMeshAreaDefinition>().OrderBy( a => a.Priority ).ThenBy( a => a.ResourcePath, StringComparer.Ordinal ) )
		{
			if ( nextId == AreaCount )
			{
				Log.Warning( $"NavMeshAreaDefinition limit reached. Max {AreaCount - FirstDefinitionArea} area definitions are currently supported." );
				break;
			}
			ids.Add( definition, nextId );
			definitions[nextId++] = definition;
		}
		Volatile.Write( ref areaRegistry, new AreaRegistry( ids, definitions ) );
	}

	internal int AreaDefinitionToId( NavMeshAreaDefinition areaDefinition )
	{
		return areaDefinition != null && Volatile.Read( ref areaRegistry ).Ids.TryGetValue( areaDefinition, out int id ) ? id : DefaultArea;
	}

	internal NavMeshAreaDefinition AreaIdToDefinition( int id ) => Volatile.Read( ref areaRegistry ).Definitions[id];

	internal int LinkTypeToId( NavMeshLinkType type )
	{
		if ( type is null ) return 0;
		if ( Volatile.Read( ref linkTypes ).Ids.TryGetValue( type, out int id ) ) return id;
		lock ( linkTypeGate )
		{
			var registry = linkTypes;
			if ( registry.Ids.TryGetValue( type, out id ) ) return id;
			if ( registry.Ids.Count == LinkTypeCount - 1 )
			{
				if ( !linkTypesFull ) Log.Warning( $"NavMeshLinkType limit reached. Max {LinkTypeCount - 1} link types are currently supported, links of type '{type.ResourcePath}' are plain links." );
				linkTypesFull = true;
				return 0;
			}
			id = registry.Ids.Count + 1;
			var types = (NavMeshLinkType[])registry.Types.Clone();
			types[id] = type;
			Volatile.Write( ref linkTypes, new LinkTypeRegistry( new Dictionary<NavMeshLinkType, int>( registry.Ids ) { [type] = id }, types ) );
			return id;
		}
	}

	// Equal filters share their cost and penalty arrays, and a filter handed out never changes
	internal NavFilter CreateFilter( NavMeshAgent agent = null )
	{
		var prices = CurrentLinkPrices();
		return new NavFilter( agent is null ? 0 : ForbiddenAreas( agent ), CurrentCosts(), agent is null ? 0 : ForbiddenLinks( agent ), prices.Costs, prices.Penalties );
	}

	// Whether CreateFilter would still give the agent this filter. Costs and penalties are as of the last navigation update.
	internal bool FilterMatches( in NavFilter filter, NavMeshAgent agent )
	{
		var prices = Volatile.Read( ref linkPrices );
		return filter.Costs == Volatile.Read( ref areaCosts ) && filter.LinkCosts == prices.Costs && filter.LinkPenalties == prices.Penalties
			&& filter.Forbidden == ForbiddenAreas( agent ) && filter.ForbiddenLinks == ForbiddenLinks( agent );
	}

	// Plain links are always open. A restricted agent forbids every other type, also those no link has used yet.
	private ulong ForbiddenLinks( NavMeshAgent agent )
	{
		if ( agent.AllowAllLinkTypes ) return 0;
		var ids = Volatile.Read( ref linkTypes ).Ids;
		ulong allowed = 1;
		foreach ( var type in agent.AllowedLinkTypes ?? [] )
		{
			if ( type is not null && ids.TryGetValue( type, out int id ) ) allowed |= 1ul << id;
		}
		return ~allowed;
	}

	// A type's cost and penalty change without notice, so the arrays are replaced when one differs
	private LinkPrices CurrentLinkPrices()
	{
		var types = Volatile.Read( ref linkTypes ).Types;
		var prices = Volatile.Read( ref linkPrices );
		for ( int id = 1; id < LinkTypeCount && types[id] is { } type; id++ )
		{
			if ( prices.Costs[id] == CostOf( type.CostMultiplier ) && prices.Penalties[id] == PenaltyOf( type.Penalty ) ) continue;
			prices = new LinkPrices( Enumerable.Repeat( 1f, LinkTypeCount ).ToArray(), new float[LinkTypeCount] );
			for ( int i = 1; i < LinkTypeCount && types[i] is { } priced; i++ ) (prices.Costs[i], prices.Penalties[i]) = (CostOf( priced.CostMultiplier ), PenaltyOf( priced.Penalty ));
			Volatile.Write( ref linkPrices, prices );
			break;
		}
		return prices;
	}

	// A definition's cost changes without notice, so the array is replaced when one differs
	private float[] CurrentCosts()
	{
		var definitions = Volatile.Read( ref areaRegistry ).Definitions;
		var costs = Volatile.Read( ref areaCosts );
		for ( int id = FirstDefinitionArea; id < AreaCount; id++ )
		{
			if ( costs[id] == CostOf( definitions[id] ) ) continue;
			costs = Enumerable.Repeat( 1f, AreaCount ).ToArray();
			for ( int i = FirstDefinitionArea; i < AreaCount; i++ ) costs[i] = CostOf( definitions[i] );
			Volatile.Write( ref areaCosts, costs );
			break;
		}
		return costs;
	}

	// Links without an area definition are always open. AllowDefaultArea is about the ground.
	private ulong ForbiddenAreas( NavMeshAgent agent )
	{
		var definitions = Volatile.Read( ref areaRegistry ).Definitions;
		ulong forbidden = agent.AllowDefaultArea ? 0 : 1ul << DefaultArea;
		for ( int id = FirstDefinitionArea; id < AreaCount && definitions[id] is { } area; id++ )
		{
			if ( (agent.AllowedAreas.Count != 0 && !agent.AllowedAreas.Contains( area )) || agent.ForbiddenAreas.Contains( area ) ) forbidden |= 1ul << id;
		}
		return forbidden;
	}

	static float CostOf( NavMeshAreaDefinition definition ) => CostOf( definition?.CostMultiplier ?? 1f );

	// The library refuses a cost below 1 and a penalty below 0, and anything not finite
	static float CostOf( float cost ) => cost >= 1f ? MathF.Min( cost, 100f ) : 1f;
	static float PenaltyOf( float penalty ) => penalty > 0f && float.IsFinite( penalty ) ? penalty : 0f;

	internal void AddArea( NavMeshArea area ) => areas.Add( area );

	internal void RemoveArea( NavMeshArea area )
	{
		areas.Remove( area );
		if ( live is null ) return;
		RemoveArea( live, area );
		if ( next is not null ) RemoveArea( next, area );
	}

	private void RemoveArea( MeshWork work, NavMeshArea area )
	{
		if ( !work.Areas.Remove( area, out var given ) ) return;
		work.Mesh.RemoveVolume( given.Id );
	}

	internal void AddLink( NavMeshLink link )
	{
		if ( !link.IsGenerated ) links.Add( link );
		else if ( live is not null ) AddGeneratedLink( live, link );
	}

	internal void RemoveLink( NavMeshLink link )
	{
		links.Remove( link );
		link.startOnNavmesh = link.endOnNavmesh = null;
		Simulation?.LinkRemoved( link );
		if ( live is null ) return;
		RemoveLink( live, link );
		if ( next is not null ) RemoveLink( next, link );
	}

	private static void RemoveLink( MeshWork work, NavMeshLink link )
	{
		if ( !work.Links.Remove( link, out var given ) ) return;
		work.Mesh.RemoveLink( given.Id );
		work.LinkComponents[given.Id] = null;
	}

	internal NavMeshLink LinkComponent( int linkId ) => live is not null && (uint)linkId < (uint)live.LinkComponents.Length ? live.LinkComponents[linkId] : null;

	// Links keep where they meet the mesh in use themselves, so any thread can read it
	private void ReadLinkEnds()
	{
		foreach ( var link in links )
		{
			System.Numerics.Vector3 start = default, end = default;
			bool startConnected = false, endConnected = false;
			if ( live is not null && live.Links.TryGetValue( link, out var given ) ) live.Mesh.GetLinkEnds( given.Id, out start, out end, out startConnected, out endConnected );
			link.startOnNavmesh = startConnected ? start : null;
			link.endOnNavmesh = endConnected ? end : null;
		}
	}

	// Compared every update: nothing tells of a changed shape struct, transform or area id
	private void UpdateAreas( MeshWork work )
	{
		foreach ( var area in areas )
		{
			var volume = area.CreateVolume( work.Mesh.Settings.AgentRadius );
			bool added = !work.Areas.TryGetValue( area, out var given );
			if ( !added && volume == given.Volume ) continue;

			int id = added ? work.Mesh.AddVolume( volume ) : given.Id;
			if ( !added ) work.Mesh.SetVolume( id, volume );

			// The library rebuilds the tiles under it from the fields it kept
			work.Areas[area] = (id, volume);
		}
	}

	private void UpdateLinks( MeshWork work )
	{
		foreach ( var link in links )
		{
			var wanted = link.CreateLink();
			bool added = !work.Links.TryGetValue( link, out var given );
			if ( !added && wanted == given.Link ) continue;

			if ( added )
			{
				Register( work, link, wanted );
				continue;
			}
			work.Mesh.SetLink( given.Id, wanted );
			work.Links[link] = (given.Id, wanted);
		}
	}

	// False when the mesh has no free link id
	private static bool Register( MeshWork work, NavMeshLink link, in NavLink wanted )
	{
		int id = work.Mesh.AddLink( wanted );
		if ( id < 0 ) return false;
		if ( work.LinkComponents.Length <= id ) Array.Resize( ref work.LinkComponents, Math.Max( 16, (id + 1) * 2 ) );
		work.LinkComponents[id] = link;
		work.Links[link] = (id, wanted);
		return true;
	}
}
