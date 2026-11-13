using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

[TestClass]
public class TileTests
{
	[TestMethod]
	public void StructsHaveTheirDesignedSizes()
	{
		Assert.AreEqual( 28, Unsafe.SizeOf<Poly>() );
		Assert.AreEqual( 16, Unsafe.SizeOf<BvNode>() );
		Assert.AreEqual( 8, Unsafe.SizeOf<PortalEdge>() );
		Assert.AreEqual( 12, Unsafe.SizeOf<ExtLink>() );
		Assert.AreEqual( 4, Unsafe.SizeOf<LinkAttach>() );
		Assert.AreEqual( 8, Unsafe.SizeOf<PolyRef>() );
	}

	[TestMethod]
	public void PolyRefSplitsSlotAndPolygonAtOnePlace()
	{
		Assert.AreEqual( 32, PolyRef.SlotBits + PolyRef.PolyBits );
		Assert.AreEqual( 262143, PolyRef.MaxTiles );
		Assert.AreEqual( 16383, PolyRef.MaxPolys );

		var poly = new PolyRef( 7, 5u << PolyRef.PolyBits | 9 );
		Assert.IsFalse( poly.IsNone || poly.IsLink );
		Assert.AreEqual( -1, poly.Link );
		Assert.AreEqual( 7u, poly.Serial );

		var link = PolyRef.ForLink( 3, 12 );
		Assert.IsTrue( link.IsLink );
		Assert.AreEqual( 12, link.Link );
		Assert.IsTrue( default( PolyRef ).IsNone );
		Assert.IsFalse( default( PolyRef ).IsLink );
	}

	[TestMethod]
	public void TreeLeavesHoldUpToFourPolygonsInTreeOrder()
	{
		var tile = GridMesh.Tile( 0, 0, new GridMesh.Layer( 0, ( x, y ) => x % 12 != 6 || y <= 8 || y >= 56 ) );
		int next = 0, leaves = 0;
		foreach ( var node in tile.Tree )
		{
			if ( node.First == BvNode.Inner ) continue;
			Assert.AreEqual( next, node.First, "Leaves must cover the polygons in order" );
			Assert.IsTrue( node.Count is >= 1 and <= BvNode.LeafSize );
			next += node.Count;
			leaves++;
		}
		Assert.AreEqual( tile.Polys.Length, next );
		Assert.IsTrue( tile.Tree.Length < tile.Polys.Length, $"{tile.Tree.Length} nodes for {tile.Polys.Length} polygons" );
		Assert.IsTrue( leaves * 2 - 1 == tile.Tree.Length );
	}

	[TestMethod]
	public void TreeNodesBoundTheirPolygons()
	{
		var tile = GridMesh.Tile( 2, -3, new GridMesh.Layer( 0, ( x, y ) => (x + y) % 7 != 0 ), new GridMesh.Layer( 200 ) );
		Check( tile, 0, out _, out _ );

		static int Check( Tile tile, int index, out Vector3 min, out Vector3 max )
		{
			var node = tile.Tree[index];
			min = tile.TreeOrigin + new Vector3( node.MinX, node.MinY, node.MinZ ) * tile.TreeCell;
			max = tile.TreeOrigin + new Vector3( node.MaxX, node.MaxY, node.MaxZ ) * tile.TreeCell;
			if ( node.First != BvNode.Inner )
			{
				for ( int p = node.First; p < node.First + node.Count; p++ )
				{
					for ( int j = 0; j < tile.Polys[p].VertCount; j++ )
					{
						var v = tile.Verts[tile.Polys[p].Verts[j]];
						Assert.IsTrue( v.X >= min.X && v.Y >= min.Y && v.Z >= min.Z && v.X <= max.X && v.Y <= max.Y && v.Z <= max.Z, "A leaf does not contain its polygon" );
					}
				}
				return index + 1;
			}

			int child = index + 1;
			while ( child < index + node.Count )
			{
				child = Check( tile, child, out var childMin, out var childMax );
				Assert.IsTrue( Vector3.Min( min, childMin ) == min && Vector3.Max( max, childMax ) == max, "A node does not contain its child" );
			}
			Assert.AreEqual( index + node.Count, child );
			return child;
		}
	}

	[TestMethod]
	public void ReorderingKeepsAdjacencySymmetric()
	{
		var tile = GridMesh.Tile( 0, 0, new GridMesh.Layer( 0, ( x, y ) => x % 12 != 6 || y <= 8 || y >= 56 ) );
		int internalEdges = 0;
		for ( int p = 0; p < tile.Polys.Length; p++ )
		{
			ref readonly var poly = ref tile.Polys[p];
			for ( int j = 0; j < poly.VertCount; j++ )
			{
				int nei = poly.Neis[j];
				if ( nei >= Poly.Border ) continue;
				internalEdges++;
				ref readonly var other = ref tile.Polys[nei];
				ushort a = poly.Verts[j], b = poly.Verts[(j + 1) % poly.VertCount];
				bool found = false;
				for ( int k = 0; k < other.VertCount; k++ )
					found |= other.Neis[k] == p && other.Verts[k] == b && other.Verts[(k + 1) % other.VertCount] == a;
				Assert.IsTrue( found, "A neighbour does not point back across the same edge" );
			}
		}
		Assert.IsTrue( internalEdges > 0 );
	}

	[TestMethod]
	public void PortalEdgesListEveryBorderEdge()
	{
		var tile = GridMesh.Flat( 1, 0 );
		Assert.AreEqual( GridMesh.Cells * 4, tile.Portals.Length );
		int previous = -1;
		foreach ( var edge in tile.Portals )
		{
			int key = edge.Poly << 8 | edge.Edge;
			Assert.IsTrue( key > previous, "Portal edges must be sorted by polygon, then edge" );
			previous = key;
			Assert.AreEqual( 1, edge.Hi - edge.Lo );
			Assert.AreEqual( Poly.Border + edge.Side, tile.Polys[edge.Poly].Neis[edge.Edge] );

			// Lo is the cell along the border where the edge starts.
			ref readonly var poly = ref tile.Polys[edge.Poly];
			Vector3 a = tile.Verts[poly.Verts[edge.Edge]], b = tile.Verts[poly.Verts[(edge.Edge + 1) % poly.VertCount]];
			float along = (edge.Side & 1) == 0 ? MathF.Min( a.Y, b.Y ) : MathF.Min( a.X, b.X ) - GridMesh.TileWorld;
			Assert.AreEqual( along, edge.Lo * GridMesh.Cell );
		}
	}

	[TestMethod]
	public void TooManyPolygonsIsAnErrorNotAnException()
	{
		var polys = new Poly[PolyRef.MaxPolys + 1];
		var tile = new TileAssembler().Create( 0, 0, new Vector3[3], polys, GridMesh.Settings, out var error );
		Assert.IsNull( tile );
		StringAssert.Contains( error, "polygons" );

		Assert.IsNull( new TileAssembler().Create( 0, 0, [], [], GridMesh.Settings, out error ) );
		Assert.IsNotNull( error );
	}
}
