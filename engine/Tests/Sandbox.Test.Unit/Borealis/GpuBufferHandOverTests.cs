using System;
using System.Threading;
using Sandbox.Rendering;

namespace BorealisTests;

/// <summary>
/// A buffer disposed across a hand-over (docs/reference/borealis-render-thread.md): while a frame handed over may draw on the
/// render thread, the main thread disposing a buffer that frame's command lists were recorded with leaves it valid for that
/// frame's replay there, and disposed for everyone else and for every later frame.
/// </summary>
[TestClass, DoNotParallelize]
public class GpuBufferHandOverTests
{
	[TestMethod]
	public void AFrameHandedOverReplaysWithABufferDisposedSince()
	{
		ThreadSafe.MarkMainThread();
		var overlapping = RenderSync.Overlapping;
		var buffer = new GpuBuffer<int>( 4 );
		var list = new CommandList( "disposed since" );
		try
		{
			bool? validInReplay = null;
			list.AddAction( () => validInReplay = buffer.IsValid );

			RenderSync.Overlapping = true;
			RenderSync.HandOver();

			// The game disposes of it for the next frame
			buffer.Dispose();
			Assert.IsFalse( buffer.IsValid, "disposed for the main thread" );

			OnRenderThread( list.Execute );
			Assert.AreEqual( true, validInReplay, "the frame handed over replays with it" );

			// The next frame handed over was recorded after the dispose
			RenderSync.HandOver();
			OnRenderThread( list.Execute );
			Assert.AreEqual( false, validInReplay, "a later frame sees it disposed" );
		}
		finally
		{
			RenderSync.Overlapping = overlapping;
		}
	}

	[TestMethod]
	public void SerialModeDisposesAtOnce()
	{
		ThreadSafe.MarkMainThread();
		var overlapping = RenderSync.Overlapping;
		var buffer = new GpuBuffer<int>( 4 );
		try
		{
			RenderSync.Overlapping = false;
			RenderSync.HandOver();
			buffer.Dispose();
			Assert.IsFalse( OnRenderThread( () => buffer.IsValid ) );
		}
		finally
		{
			RenderSync.Overlapping = overlapping;
		}
	}

	// Run on a thread marked as the render thread, and rethrow what it threw
	static void OnRenderThread( Action action ) => OnRenderThread( () => { action(); return 0; } );

	static T OnRenderThread<T>( Func<T> read )
	{
		Exception error = null;
		T result = default;
		var thread = new Thread( () =>
		{
			RenderSync.MarkRenderThread( true );
			try
			{
				result = read();
			}
			catch ( Exception e )
			{
				error = e;
			}
		} );
		thread.Start();
		thread.Join();
		if ( error is not null ) throw new AggregateException( error );
		return result;
	}
}
