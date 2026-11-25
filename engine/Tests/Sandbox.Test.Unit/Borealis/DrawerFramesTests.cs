using Sandbox.Rendering.Bridge;
using System;
using System.Threading;

namespace BorealisTests;

/// <summary>
/// The main thread and the render thread each render frames of their own at once in the editor (its tool views on the main
/// thread, the scene view's frame on the render thread): a view the main thread asks for while the render thread draws is
/// a top-level view, not the render thread's frame's child. Taken for a child, Hammer's 3D view skipped joining the render
/// thread and drew none of its worlds new to the bridge, only the grid on black.
/// </summary>
[TestClass]
public class DrawerFramesTests
{
	static void OnThread( Action marked, Action body )
	{
		Exception failure = null;
		var thread = new Thread( () =>
		{
			try { marked?.Invoke(); body(); }
			catch ( Exception e ) { failure = e; }
		} );
		thread.Start();
		Assert.IsTrue( thread.Join( 10_000 ), "thread didn't finish" );
		if ( failure is not null ) throw new AssertFailedException( failure.Message, failure );
	}

	static readonly Action MainThread = ThreadSafe.MarkMainThread;
	static readonly Action RenderThread = ThreadSafe.MarkRenderThread;

	[TestMethod]
	public void TheMainThreadsViewIsntAChildOfTheRenderThreadsFrame()
	{
		var frames = new DrawerFrames<string>();
		using var renderDrawing = new ManualResetEventSlim();
		using var renderDone = new ManualResetEventSlim();

		var render = new Thread( () =>
		{
			ThreadSafe.MarkRenderThread();
			using ( frames.Enter( "scene view" ) )
			{
				renderDrawing.Set();
				renderDone.Wait( 10_000 );
			}
		} );
		render.Start();
		Assert.IsTrue( renderDrawing.Wait( 10_000 ) );

		OnThread( MainThread, () =>
		{
			Assert.IsNull( frames.Current, "the main thread isn't in the render thread's frame" );
			using ( frames.Enter( "hammer" ) )
			{
				Assert.AreEqual( "hammer", frames.Current );
			}

			Assert.IsNull( frames.Current );
		} );

		OnThread( RenderThread, () => Assert.AreEqual( "scene view", frames.Current ) );

		renderDone.Set();
		Assert.IsTrue( render.Join( 10_000 ) );
		OnThread( RenderThread, () => Assert.IsNull( frames.Current ) );
	}

	[TestMethod]
	public void AViewAskedForWhileAFrameDrawsOnTheSameThreadIsItsChild()
	{
		var frames = new DrawerFrames<string>();
		OnThread( MainThread, () =>
		{
			using ( frames.Enter( "parent" ) )
			{
				Assert.AreEqual( "parent", frames.Current );
				using ( frames.Enter( "child" ) ) Assert.AreEqual( "child", frames.Current );
				Assert.AreEqual( "parent", frames.Current );
			}

			Assert.IsNull( frames.Current );
		} );
	}

	[TestMethod]
	public void AWorkerWorksInTheRenderThreadsFrameWhileItDrawsElseTheMainThreads()
	{
		var frames = new DrawerFrames<string>();
		using var mainDrawing = new ManualResetEventSlim();
		using var mainDone = new ManualResetEventSlim();
		var main = new Thread( () =>
		{
			ThreadSafe.MarkMainThread();
			using ( frames.Enter( "tool view" ) )
			{
				mainDrawing.Set();
				mainDone.Wait( 10_000 );
			}
		} );
		main.Start();
		Assert.IsTrue( mainDrawing.Wait( 10_000 ) );

		OnThread( null, () => Assert.AreEqual( "tool view", frames.Current ) );

		OnThread( RenderThread, () =>
		{
			using ( frames.Enter( "scene view" ) )
			{
				OnThread( null, () => Assert.AreEqual( "scene view", frames.Current ) );
			}
		} );

		OnThread( null, () => Assert.AreEqual( "tool view", frames.Current ) );
		mainDone.Set();
		Assert.IsTrue( main.Join( 10_000 ) );
		OnThread( null, () => Assert.IsNull( frames.Current ) );
	}
}
