using System;
using Sandbox.Rendering;

namespace EngineTests;

/// <summary>
/// A list's buffer upload from an array across a hand-over (docs/reference/borealis-render-thread.md): the frame handed over
/// uploads the array as it was when the list was recorded, though the main thread writes it for the next frame meanwhile (a
/// particle system's sprites) or gives it back to its pool.
/// </summary>
[TestClass, DoNotParallelize]
public class CommandListBufferDataHandOverTests
{
	[TestMethod]
	public void AFrameHandedOverUploadsTheArrayAsRecorded()
	{
		TestGraphics.Require();
		ThreadSafe.MarkMainThread();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		var previous = RenderBackend.Default;
		var overlapping = RenderSync.Overlapping;
		RenderBackend.SetDefault( borealis );
		try
		{
			using var buffer = new GpuBuffer<uint>( 4 );
			var sprites = new uint[] { 1, 2, 3, 4 };
			var list = new CommandList( "buffer data" );

			// The main thread records the next frame's upload while one is drawing, then hands it over
			RenderSync.Overlapping = true;
			RenderSync.HandOver();
			list.SetBufferData( buffer, sprites );
			RenderSync.HandOver();

			// It writes the array for the frame after while the render thread replays the list
			sprites.AsSpan().Fill( 9 );
			RenderSync.MarkRenderThread( true );
			try
			{
				list.Execute();
			}
			finally
			{
				RenderSync.MarkRenderThread( false );
			}

			var read = new uint[4];
			buffer.GetData( read, 0, 4 );
			CollectionAssert.AreEqual( new uint[] { 1, 2, 3, 4 }, read, "uploaded as recorded" );
			Assert.AreEqual( 0, borealis.ValidationErrors );
		}
		finally
		{
			RenderSync.Overlapping = overlapping;
			RenderBackend.SetDefault( previous );
		}
	}

	[TestMethod]
	public void DeferredBufferClearKeepsAllBits()
	{
		TestGraphics.Require();
		ThreadSafe.MarkMainThread();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );
		var previous = RenderBackend.Default;
		RenderBackend.SetDefault( borealis );
		try
		{
			using var buffer = new GpuBuffer<uint>( 4 );
			var commands = new CommandList();
			const uint value = 0xf1020304;
			commands.Clear( buffer, value );
			commands.Execute();

			var data = new uint[4];
			buffer.GetData( data, 0, 4 );
			CollectionAssert.AreEqual( new uint[] { value, value, value, value }, data );
			Assert.AreEqual( 0, borealis.ValidationErrors );
		}
		finally
		{
			RenderBackend.SetDefault( previous );
		}
	}
}
