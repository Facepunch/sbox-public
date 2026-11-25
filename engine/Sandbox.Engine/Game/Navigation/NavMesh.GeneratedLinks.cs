using Nav;
using Sandbox.Engine.Resources;

namespace Sandbox.Navigation;

/// <summary>
/// Generated drops and jumps are link components of the mesh in use, one game object each under one container that is
/// neither saved nor networked. The library reports them per tile and adds none itself. After every commit the components
/// of a tile whose links changed are compared with its new set by value. They are given to the mesh once and never
/// compared again, since they don't move. A mesh built aside only collects the tiles it reported until it takes over.
/// </summary>
public sealed partial class NavMesh
{
	private readonly Dictionary<(int X, int Y), List<NavMeshLink>> generatedLinks = new();
	private GameObject generatedRoot;
	private readonly List<(int X, int Y)> reportedTiles = new();

	// One pass of tiles and what each reports. Which of two tiles finds a link across their border can change between
	// builds, so components a tile gives up are spare, by value, for another tile of the pass. Membership is hashed.
	private readonly List<((int X, int Y) Tile, List<NavLink> Links)> linkPass = new();
	private readonly Dictionary<NavLink, NavMeshLink> spareLinks = new();
	private readonly HashSet<NavLink> wantedLinks = new(), keptLinks = new();

	// What the navmesh reported, whatever game code did to the component since
	private static void AddGeneratedLink( MeshWork work, NavMeshLink link )
	{
		if ( Register( work, link, link.generated with { Area = LinkArea } ) ) (link.startOnNavmesh, link.endOnNavmesh) = (link.generated.Start, link.generated.End);
	}

	private static List<NavLink> ReportedLinks( MeshWork work, (int X, int Y) tile )
	{
		var links = new List<NavLink>();
		work.Mesh.GetGeneratedLinks( tile.X, tile.Y, links );
		return links;
	}

	// After each commit of a mesh
	private void UpdateGeneratedLinks( MeshWork work )
	{
		reportedTiles.Clear();
		work.Mesh.TakeLinkChanges( reportedTiles );
		foreach ( var tile in reportedTiles ) work.LinkTiles.Add( tile );
		if ( work != live || scene is null || (work.LinkTiles.Count == 0 && (spareLinks.Count == 0 || work.Mesh.PendingTiles > 0)) ) return;

		foreach ( var tile in work.LinkTiles ) linkPass.Add( (tile, ReportedLinks( work, tile )) );
		work.LinkTiles.Clear();
		if ( SyncLinkPass() ) work.Mesh.Commit();
	}

	// The mesh in use was replaced. Components of links it has too are kept and given to it, the rest go.
	private void TakeOverLinks( Dictionary<(int X, int Y), List<NavLink>> baked = null )
	{
		if ( scene is null ) return;
		var tiles = new HashSet<(int X, int Y)>( generatedLinks.Keys );
		tiles.UnionWith( baked is null ? live.LinkTiles : baked.Keys );

		foreach ( var tile in tiles ) linkPass.Add( (tile, baked is null ? ReportedLinks( live, tile ) : baked.GetValueOrDefault( tile ) ?? new List<NavLink>()) );
		live.LinkTiles.Clear();
		if ( baked is not null ) live.BakedLinkTiles.UnionWith( baked.Keys );
		if ( SyncLinkPass() ) live.Mesh.Commit();
	}

	// Components no longer reported go, newly reported links get one, and those kept are in the mesh in use. True when the mesh changed.
	// While tiles are pending a link across a border can be gone for a commit or two, so spare components wait, disabled, until none are.
	private bool SyncLinkPass()
	{
		bool changed = false;
		foreach ( var (tile, now) in linkPass ) changed |= ReleaseLinks( tile, now );
		foreach ( var (tile, now) in linkPass ) changed |= FillLinks( tile, now );
		linkPass.Clear();
		if ( spareLinks.Count == 0 || live.Mesh.PendingTiles > 0 ) return changed;

		foreach ( var link in spareLinks.Values ) DestroyGeneratedLink( link );
		spareLinks.Clear();
		return changed;
	}

	// Keeps one component per reported link, the rest are spare or go
	private bool ReleaseLinks( (int X, int Y) tile, List<NavLink> now )
	{
		if ( !generatedLinks.TryGetValue( tile, out var had ) ) return false;
		wantedLinks.Clear();
		keptLinks.Clear();
		foreach ( var link in now ) wantedLinks.Add( link );

		bool changed = false;
		for ( int i = had.Count - 1; i >= 0; i-- )
		{
			var link = had[i];
			bool alive = link.IsValid() && !link.GameObject.IsDestroyed;
			if ( alive && wantedLinks.Contains( link.generated ) && keptLinks.Add( link.generated ) ) continue;

			had.RemoveAt( i );
			changed = true;
			if ( !alive ) continue;
			if ( !spareLinks.TryAdd( link.generated, link ) ) DestroyGeneratedLink( link );
			else link.GameObject.Enabled = false;
		}
		return changed;
	}

	// Reported links without a component take a spare one or get a new one
	private bool FillLinks( (int X, int Y) tile, List<NavLink> now )
	{
		if ( !generatedLinks.TryGetValue( tile, out var had ) ) had = new();
		keptLinks.Clear();
		foreach ( var link in had ) keptLinks.Add( link.generated );

		bool changed = false;
		foreach ( var link in had )
		{
			if ( !link.Active || live.Links.ContainsKey( link ) ) continue;
			AddGeneratedLink( live, link );
			changed = true;
		}
		foreach ( var link in now )
		{
			if ( !keptLinks.Add( link ) ) continue;
			if ( spareLinks.Remove( link, out var spare ) && spare.IsValid() && !spare.GameObject.IsDestroyed )
			{
				had.Add( spare );
				spare.GameObject.Enabled = true;
			}
			else had.Add( CreateGeneratedLink( link ) );
			changed = true;
		}

		if ( had.Count == 0 ) generatedLinks.Remove( tile );
		else generatedLinks[tile] = had;
		return changed;
	}

	private void DestroyGeneratedLink( NavMeshLink link )
	{
		RemoveLink( link );
		link.GameObject.Destroy();
	}

	private NavMeshLink CreateGeneratedLink( in NavLink link, NavMeshLinkType type = null, Vector3? climbDirection = null, float climbExitLift = 0 )
	{
		if ( !generatedRoot.IsValid() || generatedRoot.IsDestroyed )
		{
			generatedRoot = new GameObject( scene, true, "Generated NavMesh Links" );
			generatedRoot.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Hidden;
		}

		bool jump = link.Type != live.Mesh.LinkSettings.DropType;
		var go = new GameObject( generatedRoot, true, type == NavMeshLinkType.Ladder && type is not null ? "Ladder" : jump ? "Jump" : "Drop" ) { WorldPosition = link.Start };
		var component = go.Components.Create<NavMeshLink>( false );
		component.Type = type ?? (jump ? NavMeshLinkType.Jump : NavMeshLinkType.Drop);
		component.IsGenerated = true;
		component.generated = link;
		component.generatedClimbDirection = climbDirection ?? Vector3.Up;
		component.generatedClimbExitLift = climbExitLift;
		component.LocalEndPosition = link.End - link.Start;
		component.IsBiDirectional = link.Bidirectional;
		component.ConnectionRadius = link.Radius;
		component.Enabled = true;
		return component;
	}

	// The scene was cleared under them, as an editor undo does
	private void RestoreGeneratedLinks()
	{
		generatedRoot = null;
		spareLinks.Clear();
		foreach ( var components in generatedLinks.Values )
		{
			for ( int i = 0; i < components.Count; i++ ) components[i] = CreateGeneratedLink( components[i].generated );
		}
		foreach ( var entry in ladders.Values )
		{
			for ( int i = 0; i < entry.Links.Count; i++ ) entry.Links[i] = CreateGeneratedLink( entry.Links[i].generated, NavMeshLinkType.Ladder, entry.Links[i].generatedClimbDirection, entry.Links[i].ClimbExitLift );
		}
		live.Mesh.Commit();
	}

	private void DestroyGeneratedLinks()
	{
		if ( generatedRoot.IsValid() ) generatedRoot.Destroy();
		generatedRoot = null;
		generatedLinks.Clear();
		spareLinks.Clear();
		ladders.Clear();
		ladderMesh = null;
	}
}
