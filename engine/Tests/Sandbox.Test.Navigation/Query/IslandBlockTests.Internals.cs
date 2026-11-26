using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// What only the C# maps do: one built late for an older state starts from the newest map of its area mask (the C maps
/// start from the maps of the states before theirs), and what maps allocate on the managed heap.
/// </summary>
public partial class IslandBlockTests
{
	static partial void AllocatedFarLessThanAWholeMap( MeshState s, long after )
	{
		long before = GC.GetAllocatedBytesForCurrentThread();
		_ = new IslandMap( s, 0, 0, null );
		long whole = GC.GetAllocatedBytesForCurrentThread() - before;
		Console.WriteLine( $"{s.Links().Count()} links: the island map after a one-tile commit allocated {after:N0} bytes, a whole map {whole:N0}" );
		Assert.IsTrue( after * 10 < whole, $"The map allocated {after:N0} bytes against {whole:N0} for a whole map" );
	}

	[TestMethod]
	public void AStateWhoseMapIsBuiltAfterANewerOnesStartsFromTheNewerMap()
	{
		var mesh = Grid( 0, 0, 24, 16 );
		Map( mesh, Open );
		mesh.CommitTiles( [GridMesh.Quad( 20, 12 )] );
		var older = mesh.State;
		mesh.CommitTiles( [GridMesh.Quad( 3, 12 )] );
		var newer = Map( mesh, Open );
		var map = NavQuery.GetIslands( older, Open );
		AssertMatchesFlood( older, Open, map );
		Assert.AreSame( newer.BlockOf( 20, 12 ), map.BlockOf( 20, 12 ) );
		Assert.AreSame( newer.BlockOf( 16, 0 ), map.BlockOf( 16, 0 ) );
		Assert.AreNotSame( newer.BlockOf( 3, 12 ), map.BlockOf( 3, 12 ) );
	}

	[TestMethod]
	public void FirstLongSearchAfterAOneTileCommitAllocatesFarLessThanAFullMap()
	{
		// An isolated tile in the far corner: a search for it asks for the island map.
		const int Side = 96;
		var mesh = Grid( 0, 0, Side, Side, ( x, y ) => x == Side - 1 && y == Side - 1 ? GridMesh.Quad( x, y, 2, 2, 62, 62 ) : GridMesh.Quad( x, y ) );
		var query = new NavQuery( mesh );
		var path = new NavPath();
		Vector3 from = Middle( 0, 0 ), to = Middle( Side - 1, Side - 1 );
		for ( int i = 0; i < 2; i++ )
		{
			mesh.CommitTiles( [GridMesh.Quad( 40, 44 )] );
			Assert.AreEqual( PathStatus.Partial, query.FindPath( from, to, GridMesh.Snap, Open, path ) );
		}

		mesh.CommitTiles( [GridMesh.Quad( 40, 44 )] );
		long before = GC.GetAllocatedBytesForCurrentThread();
		Assert.AreEqual( PathStatus.Partial, query.FindPath( from, to, GridMesh.Snap, Open, path ) );
		long search = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.IsNotNull( mesh.State.Islands, "The search did not ask for islands" );

		before = GC.GetAllocatedBytesForCurrentThread();
		var whole = new IslandMap( mesh.State, 0, 0, null );
		long full = GC.GetAllocatedBytesForCurrentThread() - before;
		AssertMatchesFlood( mesh.State, Open, whole );
		Console.WriteLine( $"first long search after a one-tile commit allocated {search} bytes, a whole map {full}" );
		Assert.IsTrue( search * 10 < full, $"The search allocated {search} bytes against {full} for a whole map" );
	}
}
