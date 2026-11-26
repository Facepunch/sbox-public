using Borealis.Backend;
using Borealis.Backend.Meshlets;

namespace BorealisGpuTests;

/// <summary>
/// Buffers that grow into new ones while a frame records (the GPU-driven tables, the meshlet run table): what reads them
/// must read the buffer that holds what it looks up, not one read before the growth, or it reads past that buffer's end.
/// </summary>
[TestClass]
public class GrowingTablesTests : GpuTest
{
	/// <summary>
	/// A table growing moves <see cref="GpuDrivenTables.Generation"/> on and gives it a new slot, which is how a call that read
	/// the slots before knows to read them again; and the meshlet run table grows to cover every draw record before a cull
	/// reads it, since the culls read a run for every draw, with one or not.
	/// </summary>
	[TestMethod]
	public void GrownTablesAreReadAtTheirNewSlots()
	{
		using var tables = new GpuDrivenTables( Device );
		using var meshlets = new MeshletStore( Device, MeshletConfig.Default, 1 );
		var generation = tables.Generation;
		var draws = tables.Slots.Draws;

		// 1 MiB holds 32768 draw records
		for ( uint i = 0; i < 40000; i++ ) tables.Draw( 0, 0, 0, 0, i, 3, 0, 0 );
		Assert.AreNotEqual( generation, tables.Generation, "growing moves the generation on" );
		Assert.AreNotEqual( draws, tables.Slots.Draws, "at a new slot" );
		Assert.IsTrue( tables.DrawRecords >= 40000 );

		var runs = meshlets.RunSlot;
		var slot = meshlets.RunSlotCovering( tables.DrawRecords );
		Assert.AreNotEqual( runs, slot, "the run table grew for the draws it didn't cover" );
		Assert.IsTrue( meshlets.RunBytesHeld >= tables.DrawRecords * MeshletStore.RunBytes );
		Assert.AreEqual( slot, meshlets.RunSlotCovering( tables.DrawRecords ), "and doesn't grow again while it covers them" );
		Device.WaitIdle();
	}
}
