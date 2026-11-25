using System.Numerics;
using Nav.Build;

namespace Nav;

// What the mesh asks of its polygons when it makes links of what the tile builds found.
public sealed partial class NavQuery
{
	// A link is left out when walking from its start to its end is no longer than this many times the
	// straight way, as far as a search of WalkExpansions polygons can tell.
	const float WalkFactor = 4;
	const int WalkExpansions = 64;
	readonly NavPath walk = new();

	// The walk does not take links: one placed by hand must not hide the generated ones around it.
	static readonly NavFilter onFoot = NavFilter.Create().WithForbiddenLinks( ulong.MaxValue );

	// Marches outwards from a point on a boundary edge. The first mesh it comes over, when that is right
	// below the edge, is a drop and from then on the ground. A jump is the first mesh after a stretch of
	// nothing, or the first that stands higher than the ground by more than the climb. Nothing here says
	// the way is clear, nor that of the jump back up a drop. With walk, what is as short to walk is left out.
	internal void Probe( MeshState s, uint edgePoly, Vector3 edge, Vector3 outward, in NavLinkSettings search, List<FoundLink> results, bool walk )
	{
		var settings = mesh.Settings;
		float radius = settings.AgentRadius, step = MathF.Max( settings.CellSize, radius * 0.5f );
		float dropReach = 2 * radius + (2 * settings.MaxEdgeError + 2) * settings.CellSize;

		// Mesh at this height right outside the edge: a seam or a blocked area, not a ledge.
		if ( Under( s, edge + outward * radius, edge.Z - climb, edge.Z + climb, out _, out _ ) ) return;

		var start = edge - outward * radius;
		if ( Under( s, start, edge.Z - climb, edge.Z + climb, out uint startPoly, out float startZ ) ) start.Z = startZ;
		else
		{
			start = edge;
			startPoly = edgePoly;
		}

		float ground = float.NaN;
		for ( float d = 2 * radius; d <= search.Reach( settings ); d += step )
		{
			var end = edge + outward * d;
			if ( !Under( s, end, edge.Z - search.MaxDrop, edge.Z + search.MaxJumpUp, out uint endPoly, out end.Z ) ) continue;

			var kind = LinkKind.Jump;
			if ( float.IsNaN( ground ) && d <= dropReach && MathF.Abs( end.Z - edge.Z ) > climb )
			{
				if ( end.Z > edge.Z ) return;
				kind = LinkKind.Drop;
			}
			else if ( end.Z <= ground + climb || d > search.MaxJumpDistance )
			{
				ground = end.Z;
				continue;
			}
			else
			{
				var inside = end + outward * radius;
				if ( Under( s, inside, end.Z - climb, end.Z + climb, out uint insidePoly, out inside.Z ) )
				{
					end = inside;
					endPoly = insidePoly;
				}
			}

			if ( !walk || !ShortWalk( s, startPoly, start, endPoly, end ) )
			{
				bool low = edge.Z - end.Z <= search.MaxJumpUp;
				results.Add( new FoundLink { Start = start, End = end, Kind = kind, Bidirectional = kind == LinkKind.Jump && low, Back = kind == LinkKind.Drop && low } );
			}
			if ( kind == LinkKind.Jump ) return;
			ground = end.Z;
		}
	}

	// The highest polygon straight above or below the point whose plane is between low and high there.
	internal bool Under( MeshState s, Vector3 point, float low, float high, out uint id, out float z )
	{
		id = PolyRef.NoId;
		z = point.Z;
		var tile = s.Find( (int)MathF.Floor( point.X / tileWorld ), (int)MathF.Floor( point.Y / tileWorld ) );
		if ( tile == null ) return false;

		Vector3 bmin = new( point.X, point.Y, low ), bmax = new( point.X, point.Y, high );
		Span<Vector3> v = stackalloc Vector3[Poly.MaxVerts];
		var tree = tile.Tree;
		for ( int i = 0; i < tree.Length; )
		{
			ref readonly var node = ref tree[i];
			if ( !NodeBound( node, tile.TreeOrigin, tile.TreeCell, bmin, bmin, bmax, out _ ) )
			{
				i += node.First == BvNode.Inner ? node.Count : 1;
				continue;
			}
			i++;
			if ( node.First == BvNode.Inner ) continue;
			for ( int p = node.First; p < node.First + node.Count; p++ )
			{
				int n = Gather( tile, in tile.Polys[p], v );
				if ( !HeightOver( v[..n], point, out float height ) || height < low || height > high || (id != PolyRef.NoId && height <= z) ) continue;
				z = height;
				id = s.IdOf( tile, p );
			}
		}
		return id != PolyRef.NoId;
	}

	internal bool ShortWalk( MeshState s, uint from, Vector3 start, uint to, Vector3 end )
	{
		if ( from == to ) return true;
		if ( from == PolyRef.NoId || to == PolyRef.NoId ) return false;
		return FindPath( s, from, start, to, end, onFoot, walk, WalkExpansions ) == PathStatus.Complete && walk.Length <= WalkFactor * Vector3.Distance( start, end );
	}

	// Both ends on the mesh. Heights of the field against heights on polygon planes: two climbs apart at
	// most on rough ground. An end that is off the mesh by the error of its outline and a cell moves along
	// the link, the start back and the end on, until it is on it. The end of a jump was put an agent
	// radius inside the far edge by the field, and may move back that far where the mesh is narrower.
	internal bool OnMesh( MeshState s, in FoundLink link, out uint from, out uint to, out FoundLink snapped )
	{
		snapped = link;
		from = to = PolyRef.NoId;
		// In quarter cells: a start that is an edge point lies on the outline, and the polygon behind it may be narrower than a cell.
		float cell = mesh.Settings.CellSize * 0.25f;
		var along = new Vector3( link.End.X - link.Start.X, link.End.Y - link.Start.Y, 0 );
		along = along.LengthSquared() > 0 ? Vector3.Normalize( along ) * cell : default;
		int slack = 4 * ((int)MathF.Ceiling( mesh.Settings.MaxEdgeError ) + 1);
		for ( int i = 0; i <= slack && from == PolyRef.NoId; i++ )
		{
			var point = link.Start - along * i;
			if ( Under( s, point, point.Z - 2 * climb, point.Z + 2 * climb, out from, out float z ) ) snapped.Start = new Vector3( point.X, point.Y, i == 0 ? point.Z : z );
		}
		int back = link.Kind == LinkKind.Jump ? slack + 4 * BuildConfig.RadiusCells( mesh.Settings ) : 0;
		for ( int i = 0; i <= slack + back && to == PolyRef.NoId && from != PolyRef.NoId; i++ )
		{
			var point = link.End + along * (i <= slack ? i : slack - i);
			if ( Under( s, point, point.Z - 2 * climb, point.Z + 2 * climb, out to, out float z ) ) snapped.End = new Vector3( point.X, point.Y, i == 0 ? point.Z : z );
		}
		return from != PolyRef.NoId && to != PolyRef.NoId;
	}
}
