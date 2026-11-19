namespace Borealis.Gpu;

public sealed partial class Device
{
	// VK_DRIVER_ID_MESA_LLVMPIPE: Lavapipe
	const uint MesaLlvmpipe = 13;

	/// <summary>The counts in <see cref="IndirectCounts"/>: 0 to this less one.</summary>
	internal const int IndirectCountWords = 1 << 16;

	/// <summary>
	/// Where plain indirect draws are issued as counted ones (<see cref="CommandList.DrawIndirect"/>,
	/// <see cref="CommandList.DrawIndexedIndirect"/>), the counts they read: word i holds i. Invalid elsewhere.
	/// </summary>
	/// <remarks>
	/// Lavapipe (Mesa 26.2.4, and main as of 2026-11) caps every plain vkCmdDraw*Indirect at the count the last
	/// vkCmdDraw*IndirectCount in the same command buffer read: lvp_execute.c's handle_draw_indirect sets the indirect
	/// info's buffer, offset, stride and draw count but never clears indirect_draw_count, which only the count draws set
	/// (the mesh-task twin was Mesa issue 15976, fixed for mesh tasks by MR 43363). A counted draw sets its own count
	/// buffer, so there plain indirect draws read their count from here instead.
	/// </remarks>
	internal BufferHandle IndirectCounts { get; private set; }

	void CreateIndirectCounts( uint driverId, bool drawIndirectCount )
	{
		if ( driverId != MesaLlvmpipe || !drawIndirectCount ) return;

		var counts = new uint[IndirectCountWords];
		for ( int i = 0; i < counts.Length; i++ ) counts[i] = (uint)i;
		IndirectCounts = CreateBuffer( new BufferInfo { Size = counts.Length * 4L, Usage = BufferUsage.Indirect, Memory = MemoryLocation.Upload, Name = "Indirect draw counts" } );
		WriteBuffer<uint>( IndirectCounts, counts );
	}
}
