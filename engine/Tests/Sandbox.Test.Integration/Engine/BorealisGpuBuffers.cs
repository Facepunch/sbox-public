using System;
using System.Threading;

namespace EngineTests;

/// <summary>
/// Engine buffers on a Borealis.Gpu device (<see cref="BorealisGpuRenderBackend"/>): copies, fills, hidden counters and
/// asynchronous readback, recorded in a block as native records them in its render context.
/// </summary>
[TestClass]
public class BorealisGpuBuffersTest
{
	/// <summary>
	/// Values written, copied part way into another buffer, a fill of a third, and an append buffer's counter set and copied
	/// out: each read back asynchronously, after the block that recorded it.
	/// </summary>
	[TestMethod]
	public void CopiesFillsAndCountersReadBack()
	{
		// Engine buffers are made on Source 2's device too
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );

		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		uint[] copied = null, filled = null, counted = null;
		GpuBuffer[] made;
		using ( Graphics.Scope.CreateOn( borealis ) )
		{
			// Made in the block, so they start empty on its device; disposed once their readbacks are back
#pragma warning disable CA2000
			var source = new GpuBuffer<uint>( 8 );
			var destination = new GpuBuffer<uint>( 8 );
			var fill = new GpuBuffer<uint>( 4 );
			var append = new GpuBuffer<uint>( 4, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.Append );
			var count = new GpuBuffer<uint>( 4 );
#pragma warning restore CA2000
			made = [source, destination, fill, append, count];

			source.SetData( [1, 2, 3, 4, 5, 6, 7, 8] );
			destination.SetData( [0, 0, 0, 0, 0, 0, 0, 0] );
			count.SetData( [9, 9, 9, 9] );
			source.CopyTo( destination, 2, 4, 3 );
			fill.Clear( 0x12345678 );
			append.SetCounterValue( 42 );
			append.CopyStructureCount( count, 4 );

			destination.GetDataAsync( d => copied = d.ToArray() );
			fill.GetDataAsync( d => filled = d.ToArray() );
			count.GetDataAsync( d => counted = d.ToArray() );
		}

		for ( int i = 0; i < 200 && (copied is null || filled is null || counted is null); i++ )
		{
			borealis.WaitIdle();
			borealis.PollReadbacks();
			Thread.Sleep( 10 );
		}

		CollectionAssert.AreEqual( new uint[] { 0, 0, 0, 0, 3, 4, 5, 0 }, copied );
		CollectionAssert.AreEqual( new uint[] { 0x12345678, 0x12345678, 0x12345678, 0x12345678 }, filled );
		CollectionAssert.AreEqual( new uint[] { 9, 42, 9, 9 }, counted );
		Assert.AreEqual( 0, borealis.ValidationErrors );
		foreach ( var buffer in made ) buffer.Dispose();
	}
}
