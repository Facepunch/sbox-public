using Nav;
using Sandbox.Engine.Resources;

namespace Sandbox.Navigation;

public sealed partial class NavMesh
{
	private readonly record struct LadderShape( Transform Transform, Vector3 Center, Vector3 Size );
	private readonly record struct LadderLink( NavLink Link, float ExitLift );
	private sealed class LadderLinks
	{
		public LadderShape Shape;
		public RectInt Tiles;
		public readonly List<NavMeshLink> Links = new();
	}

	private readonly Dictionary<BoxCollider, LadderLinks> ladders = new();
	private HashSet<BoxCollider> ladderCandidates;
	private readonly HashSet<BoxCollider> touchedLadders = new();
	private readonly List<BoxCollider> removedLadders = new();
	private readonly List<LadderLink> wantedLadderLinks = new();
	private readonly List<Vector3> ladderEntries = new(), ladderExits = new();
	private Nav.NavMesh ladderMesh;

	// Seed once (also after hotload); collider lifecycle and inherited tag changes maintain the set.
	// Include tagged non-triggers so changing IsTrigger or a rigidbody's motion needs no rescan.
	internal void UpdateLadderCandidate( BoxCollider collider, bool active )
	{
		if ( ladderCandidates is null ) return;
		if ( active && collider.Tags.Has( "ladder" ) ) ladderCandidates.Add( collider );
		else ladderCandidates.Remove( collider );
	}

	// Triggers carry gameplay information, not walkable triangles. Discover them separately, including
	// after loading a bake. Only repeat endpoint queries and sweeps when the mesh or a trigger changes.
	private void UpdateLadderLinks( PhysicsWorld world )
	{
		if ( scene is null || live is null ) return;
		bool changed = false;
		bool meshChanged = ladderMesh != live.Mesh;
		touchedLadders.Clear();
		if ( GenerateLinks && world.IsValid() && NavMeshLinkType.Ladder is not null )
		{
			if ( ladderCandidates is null )
			{
				ladderCandidates = new();
				foreach ( var collider in scene.Query<BoxCollider>() ) UpdateLadderCandidate( collider, collider.Active );
			}
			foreach ( var collider in ladderCandidates )
			{
				if ( !collider.Active || !collider.IsTrigger || collider.IsDynamic ) continue;
				touchedLadders.Add( collider );
				var shape = new LadderShape( collider.WorldTransform, collider.Center, collider.Scale );
				if ( !ladders.TryGetValue( collider, out var entry ) ) ladders[collider] = entry = new();
				else if ( !meshChanged && shape == entry.Shape && !LadderTilesChanged( entry.Tiles ) && entry.Links.All( static x => x.IsValid() && !x.GameObject.IsDestroyed ) ) continue;

				entry.Shape = shape;
				wantedLadderLinks.Clear();
				FindLadderLinks( world, collider, wantedLadderLinks, out entry.Tiles );
				for ( int i = entry.Links.Count - 1; i >= 0; i-- )
				{
					var link = entry.Links[i];
					if ( link.IsValid() && !link.GameObject.IsDestroyed && link.ClimbDirection == collider.WorldRotation.Up && wantedLadderLinks.Remove( new( link.generated, link.ClimbExitLift ) ) )
					{
						if ( link.Active && !live.Links.ContainsKey( link ) )
						{
							AddGeneratedLink( live, link );
							changed = true;
						}
						continue;
					}
					if ( link.IsValid() ) DestroyGeneratedLink( link );
					entry.Links.RemoveAt( i );
					changed = true;
				}
				foreach ( var link in wantedLadderLinks )
				{
					entry.Links.Add( CreateGeneratedLink( link.Link, NavMeshLinkType.Ladder, collider.WorldRotation.Up, link.ExitLift ) );
					changed = true;
				}
			}
		}

		removedLadders.Clear();
		foreach ( var (collider, entry) in ladders )
		{
			if ( touchedLadders.Contains( collider ) ) continue;
			foreach ( var link in entry.Links ) if ( link.IsValid() ) DestroyGeneratedLink( link );
			removedLadders.Add( collider );
			changed |= entry.Links.Count > 0;
		}
		foreach ( var collider in removedLadders ) ladders.Remove( collider );
		if ( changed ) live.Mesh.Commit();
		ladderMesh = live.Mesh;
		live.LadderTiles.Clear();
	}

	private bool LadderTilesChanged( RectInt bounds )
	{
		foreach ( var (x, y) in live.LadderTiles )
			if ( x >= bounds.Left && x <= bounds.Right && y >= bounds.Top && y <= bounds.Bottom ) return true;
		return false;
	}

	private void FindLadderLinks( PhysicsWorld world, BoxCollider collider, List<LadderLink> result, out RectInt tiles )
	{
		tiles = default;
		var up = collider.WorldRotation.Up;
		if ( MathF.Abs( up.z ) < NavMeshLink.MinimumClimbUp ) return;
		if ( up.z < 0 ) up = -up;
		var size = collider.Scale * collider.WorldScale;
		size = new Vector3( MathF.Abs( size.x ), MathF.Abs( size.y ), MathF.Abs( size.z ) );
		var settings = live.Mesh.Settings;
		if ( !float.IsFinite( size.Length ) || size.z <= settings.AgentClimb || MathF.Min( size.x, size.y ) <= 0 ) return;

		// Same thin-axis convention as MoveModeLadder, including a collider center offset from its object.
		var normal = size.x <= size.y ? collider.WorldRotation.Forward : collider.WorldRotation.Left;
		// Keep the character upright beside the tilted face, with its feet following the ladder's axis.
		float horizontalNormal = normal.WithZ( 0 ).Length;
		normal = normal.WithZ( 0 ) / horizontalNormal;
		var center = collider.WorldTransform.PointToWorld( collider.Center );
		float face = MathF.Min( size.x, size.y ) * 0.5f / horizontalNormal;
		float radius = settings.AgentRadius;
		float reach = face + radius + CellSize * 2;
		float heightTolerance = settings.AgentClimb + CellHeight * 2;
		var extents = new Vector3( CellSize * 2, CellSize * 2, heightTolerance );
		var bottom = center - up * size.z * 0.5f;
		var top = center + up * size.z * 0.5f;
		// Include the candidate search boxes, swept body radius and the tilted step-over extension.
		float padding = reach + radius * 2 + CellSize * 2
			+ (heightTolerance * 2 + settings.AgentClimb) * up.WithZ( 0 ).Length / up.z;
		var bounds = new BBox( Vector3.Min( bottom, top ), Vector3.Max( bottom, top ) ).Grow( padding );
		var minTile = WorldPositionToTilePosition( bounds.Mins );
		tiles = new RectInt( minTile, WorldPositionToTilePosition( bounds.Maxs ) - minTile );
		var query = new NavQuery( live.Mesh );
		var filter = CreateFilter();

		// FindNearest bounds polygon AABBs, not the returned point. Explicitly bound the result too,
		// otherwise a diagonal polygon can attach a ladder to a distant edge outside this search box.
		bool FindLanding( Vector3 target, out Vector3 point )
		{
			bool found = query.FindNearest( target, extents, filter, out _, out var nearest );
			point = nearest;
			var delta = point - target;
			return found && MathF.Abs( delta.x ) <= extents.x && MathF.Abs( delta.y ) <= extents.y && MathF.Abs( delta.z ) <= extents.z;
		}

		ladderExits.Clear();
		for ( int side = -1; side <= 1; side += 2 )
			for ( float inset = 0; inset <= radius; inset += CellSize )
				if ( FindLanding( top + normal * ((reach + inset) * side), out var end ) && !ladderExits.Contains( end ) ) ladderExits.Add( end );

		// Map-authored triggers need not end at the ideal place for an NPC's feet. Search a radius
		// farther out on either face, keeping height and horizontal reach bounded to this ladder.
		for ( int side = -1; side <= 1; side += 2 )
		{
			LadderLink? best = null;
			float bestDistance = float.MaxValue;
			ladderEntries.Clear();
			for ( float entryInset = 0; entryInset <= radius; entryInset += CellSize )
			{
				if ( !FindLanding( bottom + normal * ((reach + entryInset) * side), out var start ) || ladderEntries.Contains( start ) ) continue;
				ladderEntries.Add( start );
				foreach ( var end in ladderExits )
				{
					if ( end.z - start.z <= settings.AgentClimb ) continue;
					float distance = start.Distance( end );
					if ( distance >= bestDistance || !TryLadderExit( world, start, end, up, out var lift ) ) continue;
					distance += lift * 2;
					if ( distance >= bestDistance ) continue;
					bestDistance = distance;
					best = new( new NavLink
					{
						Start = start,
						End = end,
						Radius = CellSize * 2,
						Bidirectional = true,
						Area = LinkArea,
						Type = (byte)LinkTypeToId( NavMeshLinkType.Ladder )
					}, lift );
				}
			}
			if ( best is { } found && !result.Contains( found ) ) result.Add( found );
		}
	}

	// A small top rung or roof rim is a step, not a blocked ladder. Validate all three legs and
	// retain the lift for traversal; accepting an elevated sweep alone would send NPCs through the rim.
	private bool TryLadderExit( PhysicsWorld world, Vector3 start, Vector3 end, Vector3 up, out float lift )
	{
		float step = live.Mesh.Settings.AgentClimb;
		for ( lift = 0; ; lift = MathF.Min( lift + CellHeight, step ) )
		{
			var landing = end + Vector3.Up * lift;
			var above = start + up * ((landing.z - start.z) / up.z);
			// Higher exits only extend this same climb segment, so a blocked climb cannot be rescued by lifting it.
			if ( !LadderPathClear( world, start, above ) ) return false;
			if ( LadderPathClear( world, above, landing ) && LadderPathClear( world, landing, end ) ) return true;
			if ( lift >= step ) return false;
		}
	}

	private bool LadderPathClear( PhysicsWorld world, Vector3 start, Vector3 end )
	{
		var settings = live.Mesh.Settings;
		// Leave a small skin above polygon-plane heights. Rigidbody actors and props are transient,
		// even when grounded/frozen and therefore keyframed. They must not erase a persistent link.
		float radius = MathF.Max( settings.AgentRadius, 1 );
		var trace = world.Trace.Cylinder( settings.AgentHeight - 1, radius,
			start + Vector3.Up * (settings.AgentHeight * 0.5f + 1),
			end + Vector3.Up * (settings.AgentHeight * 0.5f + 1) );
		trace.filterCallback = shape => shape.Body.Component is not Rigidbody && IsBodyRelevantForNavmesh( shape.Body );
		var hit = trace.Run();
		return !hit.Hit && !hit.StartedSolid;
	}
}
