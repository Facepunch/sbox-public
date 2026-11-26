using Borealis.Backend;
using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// GPU-driven index pages (docs/reference/borealis-gpu-driven.md, "Generated LODs"): meshes' indices fill a page from its
/// start and generated levels from its end, and a page keeps free only the room for the levels its meshes' ranges may get.
/// </summary>
[TestClass]
public class IndexPageTests : GpuTest
{
	[TestMethod]
	public void PagesKeepRoomOnlyForLevelCandidatesAndFillLevelsFromTheEnd()
	{
		using var tables = new GpuDrivenTables( Device ) { LevelRoom = 0.5f };

		// A mesh whose ranges may all get levels keeps half its indices again free; one with none keeps nothing
		var (_, slot, first) = tables.AddIndices( new byte[64 << 10], IndexFormat.UInt16, rangeBytes: 64 << 10 );
		Assert.AreEqual( 0u, first );
		Assert.AreEqual( (32L << 10, 0L), tables.LevelBytes, "room for the candidate's levels, none used" );
		var (_, sameSlot, second) = tables.AddIndices( new byte[16 << 10], IndexFormat.UInt16 );
		Assert.AreEqual( slot, sameSlot );
		Assert.AreEqual( (uint)(32 << 10), second, "after the first mesh's indices: the room isn't between them" );
		Assert.AreEqual( 32L << 10, tables.LevelBytes.Room );

		// The page is no bigger than a page: levels go at its end, the first right at it, each below the last, taking the room
		Assert.IsTrue( tables.TryAddLevelIndices( slot, IndexFormat.UInt16, 1000, out _, out var level ) );
		Assert.AreEqual( (uint)((GpuDrivenTables.IndexPageSize - 1008) / 2), level );
		Assert.IsTrue( tables.TryAddLevelIndices( slot, IndexFormat.UInt16, 2000, out _, out var next ) );
		Assert.AreEqual( level - 2000 / 2, next );
		Assert.AreEqual( ((32L << 10) - 3008, 3008L), tables.LevelBytes );

		// A mesh that would cut into the room left goes in a new page; one that fits doesn't
		var free = GpuDrivenTables.IndexPageSize - (80 << 10) - 3008 - ((32 << 10) - 3008);
		var (_, fits, _) = tables.AddIndices( new byte[free], IndexFormat.UInt16 );
		Assert.AreEqual( slot, fits, "exactly the space between meshes and the room kept" );
		var (_, other, otherFirst) = tables.AddIndices( new byte[16], IndexFormat.UInt16 );
		Assert.AreNotEqual( slot, other );
		Assert.AreEqual( 0u, otherFirst );

		// The full page's room is still there for its levels
		Assert.IsTrue( tables.TryAddLevelIndices( slot, IndexFormat.UInt16, (32 << 10) - 3008, out _, out _ ) );
		Assert.IsFalse( tables.TryAddLevelIndices( slot, IndexFormat.UInt16, 16, out _, out _ ), "and then it's full" );
		Device.WaitIdle();
	}
}
