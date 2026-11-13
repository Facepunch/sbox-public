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

	// Equal filters share their costs array, and a filter handed out never changes
	internal NavFilter CreateFilter( NavMeshAgent agent = null )
	{
		return new NavFilter( agent is null ? 0 : ForbiddenAreas( agent ), CurrentCosts() );
	}

	// Whether CreateFilter would still give the agent this filter. Costs are as of the last navigation update.
	internal bool FilterMatches( in NavFilter filter, NavMeshAgent agent )
	{
		return filter.Costs == Volatile.Read( ref areaCosts ) && filter.Forbidden == ForbiddenAreas( agent );
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

	// The library refuses a cost below 1
	static float CostOf( NavMeshAreaDefinition definition )
	{
		float cost = definition?.CostMultiplier ?? 1f;
		return cost >= 1f ? MathF.Min( cost, 100f ) : 1f;
	}

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

	internal void AddLink( NavMeshLink link ) => links.Add( link );

	internal void RemoveLink( NavMeshLink link )
	{
		links.Remove( link );
		link.startOnNavmesh = link.endOnNavmesh = null;
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

			int id = given.Id;
			if ( added )
			{
				id = work.Mesh.AddLink( wanted );
				if ( id < 0 ) continue;
				if ( work.LinkComponents.Length <= id ) Array.Resize( ref work.LinkComponents, Math.Max( 16, (id + 1) * 2 ) );
				work.LinkComponents[id] = link;
			}
			else work.Mesh.SetLink( id, wanted );
			work.Links[link] = (id, wanted);
		}
	}
}
