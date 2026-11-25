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
		TestGraphics.Require();

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

	/// <summary>
	/// A buffer cleared and then written in part outside a block, as a clutter batch marks every sphere slot free and then
	/// uploads its tiles: the write is recorded after the fill in the uploads and waits for it, so it reads back over the
	/// clear. A large buffer, so a fill left to run beside the write would finish after it.
	/// </summary>
	[TestMethod]
	public void WritesAfterAClearOutsideABlockKeepTheirValues()
	{
		TestGraphics.Require();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		var previous = RenderBackend.Default;
		RenderBackend.SetDefault( borealis );
		try
		{
			for ( int round = 0; round < 8; round++ )
			{
				using var buffer = new GpuBuffer<uint>( 1 << 22 );
				buffer.Clear( 0xBF800000 );
				buffer.SetData( new uint[] { 1, 2, 3, 4 }, 0 );
				buffer.SetData( new uint[] { 5, 6 }, 1 << 21 );

				var start = new uint[5];
				buffer.GetData( start, 0, 5 );
				CollectionAssert.AreEqual( new uint[] { 1, 2, 3, 4, 0xBF800000 }, start, $"round {round}: the start" );
				var middle = new uint[2];
				buffer.GetData( middle, 1 << 21, 2 );
				CollectionAssert.AreEqual( new uint[] { 5, 6 }, middle, $"round {round}: the middle" );
			}

			Assert.AreEqual( 0, borealis.ValidationErrors );
		}
		finally
		{
			RenderBackend.SetDefault( previous );
		}
	}

	/// <summary>
	/// A buffer the game writes every update and reads every frame, the frame's read with nothing after it in its list (a
	/// block's draws end with no barrier over the buffers they read): each write outside a block, in the list of uploads,
	/// lands after the last frame's read with nothing reported by synchronization validation, and the last read sees the last
	/// write. (DeviceTests.WritesAfterAnEarlierSubmissionsReadWaitForIt shows the hazard without the list's barrier.)
	/// </summary>
	[TestMethod]
	public void WritesOutsideABlockWaitForTheLastFramesReads()
	{
		TestGraphics.Require();
		var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true, syncValidation: true, message: m =>
		{
			if ( m.Severity == Borealis.Gpu.MessageSeverity.Error ) errors.Enqueue( m.Id );
		} );

		var previous = RenderBackend.Default;
		RenderBackend.SetDefault( borealis );
		try
		{
			using var buffer = new GpuBuffer<uint>( 64 );
			using var copy = new GpuBuffer<uint>( 64 );
			var gpu = borealis.Device;
			for ( uint frame = 1; frame <= 3; frame++ )
			{
				buffer.SetData( Enumerable.Repeat( frame, 64 ).ToArray() );

				// The uploads go before the frame's block; the frame reads the buffer with nothing after the read
				using ( Graphics.Scope.CreateOn( borealis ) ) { }
				var reads = gpu.BeginCommandList( Borealis.Gpu.QueueType.Graphics, "The frame's reads" );
				reads.CopyBuffer( borealis.BufferOf( buffer ), 0, borealis.BufferOf( copy ), 0, 64 * sizeof( uint ) );
				gpu.Submit( reads );
			}

			borealis.WaitIdle();
			var read = new uint[64];
			copy.GetData( read, 0, 64 );
			CollectionAssert.AreEqual( Enumerable.Repeat( 3u, 64 ).ToArray(), read, "the last frame read the last write" );
			Assert.AreEqual( 0, borealis.ValidationErrors, $"Reported: {string.Join( ", ", errors )}" );
		}
		finally
		{
			RenderBackend.SetDefault( previous );
		}
	}

	/// <summary>
	/// A synchronous read waits for the GPU (and with a render thread, for it): each is counted at the sync point's counter
	/// (<see cref="Sandbox.Rendering.RenderSync.Readbacks"/>, <c>r_sync_readbacks</c>), and still reads what was written.
	/// </summary>
	[TestMethod]
	public void SynchronousReadsAreCounted()
	{
		TestGraphics.Require();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		var previous = RenderBackend.Default;
		RenderBackend.SetDefault( borealis );
		try
		{
			using var buffer = new GpuBuffer<uint>( 16 );
			buffer.SetData( new uint[] { 7, 8, 9 }, 0 );

			var before = Sandbox.Rendering.RenderSync.Readbacks;
			var read = new uint[3];
			for ( int i = 0; i < 3; i++ ) buffer.GetData( read, 0, 3 );

			CollectionAssert.AreEqual( new uint[] { 7, 8, 9 }, read );
			Assert.AreEqual( before + 3, Sandbox.Rendering.RenderSync.Readbacks, "each read counted" );
			Assert.AreEqual( 0, borealis.ValidationErrors );
		}
		finally
		{
			RenderBackend.SetDefault( previous );
		}
	}

	/// <summary>
	/// A buffer a game's drawing made while the extract recorded it (on the recording backend, which holds nothing) starts empty
	/// on Borealis.Gpu: its first write can come in a block, as the recording's replay on the render thread makes it, without a
	/// read back (which a block can't do), and reads back what was written.
	/// </summary>
	[TestMethod]
	public void ABufferMadeInARecordingIsFirstWrittenInABlock()
	{
		TestGraphics.Require();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );
		var previous = RenderBackend.Default;
		RenderBackend.SetDefault( borealis );
		try
		{
			MadeInARecordingThenWrittenInABlock( borealis );
		}
		finally
		{
			RenderBackend.SetDefault( previous );
		}
	}

	static void MadeInARecordingThenWrittenInABlock( BorealisGpuRenderBackend borealis )
	{
		GpuBuffer<uint> buffer;
		var recording = new Sandbox.Rendering.DrawRecording();
		var recorder = new Sandbox.Rendering.RecordingRenderBackend();
		using ( var view = new Sandbox.Rendering.RecordingFrameView { Into = recording, Facts = new Sandbox.Rendering.GameDrawExtract.ViewFacts { CubeFace = -1 } } )
		{
			recorder.Begin( recording, new NativeEngine.RenderViewport( new Rect( 0, 0, 64, 64 ), 0, 1 ) );
			try
			{
				using var scope = Graphics.RecordingScope.Create( recorder, view, recording, SceneLayerType.Opaque, ImageFormat.RGBA16161616F, MultisampleAmount.MultisampleNone );
#pragma warning disable CA2000 // disposed once its readback is back
				buffer = new GpuBuffer<uint>( 4 );
#pragma warning restore CA2000
			}
			finally
			{
				recorder.End();
			}
		}

		uint[] read = null;
		using ( Graphics.Scope.CreateOn( borealis ) )
		{
			buffer.SetData( [1, 2, 3, 4] );
			buffer.GetDataAsync( d => read = d.ToArray() );
		}

		for ( int i = 0; i < 200 && read is null; i++ )
		{
			borealis.WaitIdle();
			borealis.PollReadbacks();
			Thread.Sleep( 10 );
		}

		CollectionAssert.AreEqual( new uint[] { 1, 2, 3, 4 }, read );
		Assert.AreEqual( 0, borealis.ValidationErrors );
		buffer.Dispose();
	}
}
